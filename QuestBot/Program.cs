using System;
using System.Linq;
using System.Threading;
using Discord;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace QuestBot
{

    class Quest
    {
        //Current votes [UserID, choice]
        public ConcurrentDictionary<ulong, int> votes;
        //Other options [UserID, other_idea]
        public ConcurrentDictionary<ulong, string> others;
        //UserID of the quest giver
        public ulong questGiver;

        public Quest(ulong questGiver)
        {
            votes = new ConcurrentDictionary<ulong, int>();
            others = new ConcurrentDictionary<ulong, string>();
            this.questGiver = questGiver;
        }
        public void Reset()
        {
            votes.Clear();
            others.Clear();
        }
    }

    class Roll
    {
        //For the following comments, example of a roll: 5d6d1 + 7 A

        //The number of dice (null is presumed 1 die roll), ex 5
        public int? NumDice = null;
        //The die to roll, ex 6
        public int Die = 0;
        //If the Modifier is negative, ex false
        public bool Mod = false;
        //The modifier, ex 7
        public int ModNum = 0;
        //If we roll Advantage (true), Disadvantage (false) or neither (null), ex true
        public bool? Adv = null;
        //How many dice do we drop?, ex 1
        public int Drop = 0;
    }

    class Program : IDisposable
    {
        //Our bot
        private DiscordClient bot;

        //Quests in each quest
        private ConcurrentDictionary<ulong, Quest> channels;

        private Regex dieRegex;
        private Regex randRegex;
        private static object lockObj = new object();
        private Random rand;

        public Program(string bot_username, string bot_password)
        {
            dieRegex = new Regex(@"^(?<NumDice>\d{0,2}) *d *(?<Die>\d{1,2}) *(d(?<Drop>\d{1,2}))? *((?<Mod>[+-]) *(?<ModNum>\d{1,3}))? *(?<Adv>adv|dis)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            randRegex = new Regex(@"^(?<From>\d{1,9}) *- *(?<To>\d{1,9})", RegexOptions.Compiled);

            rand = new Random();
            bot = new DiscordClient();

            channels = new ConcurrentDictionary<ulong, Quest>();

            bot.MessageReceived += BotMessageRecievedWrapper;

            Console.WriteLine("Booting up...");

            //Can I make this simpiler? Need to wait to correctly say we've started. 
            new Task(async () => {
                //wait till we login
                await bot.Connect(bot_username, bot_password);
                //set game name
                bot.SetGame("May the dice be ever in your favor.");
                //now we've started
                Console.WriteLine("We've started!");
            }).RunSynchronously();
        }

        //Wrapper to handle exceptions
        //though this should only occur on Discord's side
        //My code is (as far as I'm aware) exception free.
        private void BotMessageRecievedWrapper(object sender, MessageEventArgs e)
        {
            //This method is what we in the business call an afterthought.
            try
            {
                BotMessageReceived(sender, e);
            }
            catch(Exception ex)
            {
                try { e.Channel.SendMessage("An error occured. Try reformatting your query.");  } catch(Exception) { /* Silently handle */ }
                Console.WriteLine("ERROR:" + ex.Message);
                Console.WriteLine("ERROR:" + ex.StackTrace);
            }
        }

        //Main handler for all the commands
        private void BotMessageReceived(object sender, MessageEventArgs e)
        {
            if (e.Message.IsAuthor) return;

            //show help
            if(e.Message.Text.StartsWith(".help"))
            {
                Console.WriteLine(">>help");
                PrintHelp(e);
                return;
            }

            //roll a die
            if (e.Message.Text.StartsWith(".roll"))
            {
                Console.WriteLine(">>roll");
                HandleRoll(e);
                return;
            }
            //random number generator
            else if (e.Message.Text.StartsWith(".rand"))
            {
                Console.WriteLine(">>rand");
                HandleRand(e);
                return;
            }

            Quest quest = null;
            channels.TryGetValue(e.Channel.Id, out quest);

            #region Quest
            if (e.Message.Text.StartsWith(".startsession"))
            {
                Console.WriteLine(">>startsession");

                if (quest == null)
                {
                    channels[e.Channel.Id] = new Quest(e.User.Id);
                    e.Channel.SendMessage("Quest beginning!");
                }
                else
                {
                    e.Channel.SendMessage(e.User.Mention + " quest already in progress.");
                }
            }
            else if (quest != null)
            {
                if (e.Message.Text.StartsWith(".endsession"))
                {
                    Console.WriteLine(">>endsession");

                    if (e.User.Id == quest.questGiver)
                    {
                        Quest empty;
                        channels.TryRemove(e.Channel.Id, out empty);
                        e.Channel.SendMessage("Quest ended!");
                    }
                    else
                    {
                        e.Channel.SendMessage(e.User.Mention + " only the quest starter can end the quest.");
                    }
                }
                else if (e.Message.Text.StartsWith(".vote"))
                {
                    Console.WriteLine(">>vote");

                    int result;
                    var str = e.Message.Text.Substring(5);

                    if (int.TryParse(str, out result))
                    {
                        string empty;
                        quest.others.TryRemove(e.User.Id, out empty);
                        quest.votes[e.User.Id] = result;
                    }
                    else
                    {
                        e.Channel.SendMessage(e.User.Mention + " '" + str + "' is not a number");
                    }
                }
                else if (e.Message.Text.StartsWith(".other"))
                {
                    Console.WriteLine(">>other");

                    int empty;
                    quest.votes.TryRemove(e.User.Id, out empty);

                    quest.others[e.User.Id] = e.Message.Text.Substring(6);
                }
                else if (e.Message.Text.StartsWith(".next"))
                {
                    Console.WriteLine(">>next");

                    if (e.User.Id == quest.questGiver)
                    {
                        quest.Reset();
                        e.Channel.SendMessage("Next chapter");
                    }
                    else
                    {
                        e.Channel.SendMessage(e.User.Mention + " Only the quest starter can go to the next chapter.");
                    }

                }
                else if (e.Message.Text.StartsWith(".tally"))
                {
                    string msg = "Tally: \n";

                    //count all the votes
                    foreach (var cnt in quest.votes.Select(x => x.Value).Distinct())
                    {
                        msg += "#" + cnt + " : " + quest.votes.Where(x => x.Value == cnt).Count() + "\n";
                    }

                    //if we don't have any other options, don't show it
                    if (quest.others.Any())
                    {
                        msg += "Others include: \n";
                        //can't make this a for loop because indexes are not linear, they're userIDs
                        int cntr = 1;
                        foreach (var o in quest.others)
                        {
                            msg += "#" + cntr + " : " + o.Value + "\n";
                            cntr++;
                        }
                    }

                    e.Channel.SendMessage(msg);
                }
            }
            #endregion
        }

        //Generates a new integer
        private int NextInt(int from, int to)
        {
            //Random isn't thread safe
            lock(lockObj)
            {
                return rand.Next(from, to);
            }
        }

        //Print's help
        private void PrintHelp(MessageEventArgs e)
        {
            string msg = "**QuestBot commands:** \n\n" +
                         "*General commands:*\n" +
                         "**.help**                      Show this help text\n" +
                         "**.rand [Num1]-[Num2]**\n" +
                         "    Generate a random number between Num1 and Num2\n" +
                         "**.roll [Num]?d[Num](d[Num])?(+|-[Num])?[Adv|Dis]?**\n" +
                         "    Roll a die with modifiers, drops and advantage/dis.\n" +
                         "\n*Quest Commands:*\n" +
                         "**.startsession**        Start a quest\n" +
                         "**.endsession**          End a quest\n" +
                         "**.vote [Number]**  Vote in a quest chapter\n" +
                         "**.other [String]**     Give another answer than currently votable\n" +
                         "**.tally**                      Tally the current votes in this chapter\n" +
                         "**.next**                       Go to the next chapter in a quest\n" +
                         "\n*Questing information:*\n" +
                         "To start a session, the quest giver runs '.startsession'. He/she is now " +
                         "in control of ending the quest and moving to the next chapter. " +
                         "The quest giver writes out the current chapter of the quest and " +
                         "includes options to vote on with '.vote'. If none of the options seem appealing " +
                         "the quest giver may allow other answers to be posted with '.other'. A " +
                         "tally can be conducted once everyone has finished voting with '.tally' and " +
                         "the quest giver may move to the next chapter with '.next'. This resets all " +
                         "votes and voting can start again. When the session is done, the quest giver " +
                         "can finish the quest by typing '.endsession'. There can only be one session " +
                         "per channel at any time.";


            e.Channel.SendMessage(msg);

        }

        //Handle generating random numbers
        private void HandleRand(MessageEventArgs e)
        {
            if (e.Message.Text.Length < 6)
            {
                e.Channel.SendMessage("Invalid range. Expected [Num]-[Num]. Example '.rand 1-100'");
                return;
            }

            //Match the string to the regex
            var randStr = e.Message.Text.Substring(5).Trim();

            Match m = randRegex.Match(randStr);

            int from = -1;
            int to = -1;
            if(m.Success)
            {
                //extract the information using group names
                //yes you can use indexes, but this way is easier and I'm lazy
                foreach(var name in randRegex.GetGroupNames())
                {
                    Group g = m.Groups[name];
                    if(g.Success)
                    {
                        if (name == "From")
                            from = int.Parse(g.Value);
                        else if (name == "To")
                            to = int.Parse(g.Value);
                    }
                }

                if (from > to)
                    e.Channel.SendMessage("Invalid range.");
                else
                    e.Channel.SendMessage("**" + NextInt(from, to) + "**");
            }
            else
            {
                e.Channel.SendMessage("Invalid range. Expected [Num]-[Num]. Example '.rand 1-100'");
            }
        }

        //Handle the rolling of dice
        private void HandleRoll(MessageEventArgs e)
        {
            if (e.Message.Text.Length < 6)
            {
                e.Channel.SendMessage("Invalid roll. Expected [Num]?d[Num](d[Num])? (+|-[Num])? [Adv|Dis]?. Example '.roll d6 adv' or '.roll 4d6d1 + 1'");
                return;
            }

            //Match the string to the regex
            var rollStr = e.Message.Text.Substring(5).Trim();

            Match m = dieRegex.Match(rollStr);

            Roll roll = new Roll();
            if (m.Success)
            {
                //extract the information using group names
                //yes you can use indexes, but it gets very tricky
                foreach (var name in dieRegex.GetGroupNames())
                {
                    Group g = m.Groups[name];
                    if (g.Success)
                    {
                        //something, something, something not a master at writing regex
                        //Some of the groups get captured as empty if they are
                        //Which is perfectly ok...but we need to not add them if they are
                        bool notEmpty = !string.IsNullOrEmpty(g.Value);

                        if (name == "NumDice" && notEmpty)
                            roll.NumDice = int.Parse(g.Value);
                        else if (name == "Die")
                            roll.Die = int.Parse(g.Value);
                        else if (name == "Mod" && notEmpty)
                            roll.Mod = g.Value == "-" ? true : false;
                        else if (name == "ModNum" && notEmpty)
                            roll.ModNum = int.Parse(g.Value);
                        else if (name == "Adv" && notEmpty)
                            roll.Adv = g.Value.ToLower() == "adv";
                        else if (name == "Drop" && notEmpty)
                            roll.Drop = int.Parse(g.Value);
                    }
                }

                if(((roll.NumDice == null || roll.NumDice == 0) && roll.Drop > 0)
                    ||(roll.Drop >= roll.NumDice))
                {
                    e.Channel.SendMessage("Invalid number of dice dropped. Must be strictly less than the total number of dice rolled.");
                    return;
                }

                string msg = "";
                //0d{x} = roll no dice. Only print modifier
                if(roll.NumDice == 0)
                {
                    msg += "**[" + (roll.Mod ? "-" : "") + roll.ModNum + "]**";
                }
                //1d{x} = roll 1 die only.
                else if(roll.NumDice == null || roll.NumDice == 1)
                {
                    int first = NextInt(1, roll.Die + 1) + (roll.Mod ? -1 : 1) * roll.ModNum;
                    int second = 0;

                    //roll advantage/disadvantage?
                    if (roll.Adv != null) second = NextInt(1, roll.Die + 1) + (roll.Mod ? -1 : 1) * roll.ModNum;

                    if (roll.Adv == true)
                    {
                        msg += "{ [" + first + "], [" + second + "] } = **[" + Math.Max(first, second) + "]**";
                    }
                    else if (roll.Adv == false)
                    {
                        msg += "{ [" + first + "], [" + second + "] } = **[" + Math.Min(first, second) + "]**";
                    }
                    else
                    {
                        //no advantage/disadvantage
                        msg += "**[" + first + "]**";
                    }
                }
                //{y}d{x} = roll multiple dice
                else if(roll.NumDice > 1)
                {
                    List<int>[] fullRolls = new List<int>[2];

                    //int[] rolls = { (roll.Mod ? -1 : 1) * roll.ModNum,
                    //                (roll.Mod ? -1 : 1) * roll.ModNum };

                    //This whole section is ugly and it works!
                    //If we have to roll normally, only loops once, if we have to roll twice, it loops twice
                    for (int i = 0; i < 1 + (roll.Adv != null ? 1 : 0); i++)
                    {
                        fullRolls[i] = new List<int>((int)roll.NumDice);

                        //generate X number of rolls, roll a number for that, add it to the current counter, 
                        for (int x = 0; x < roll.NumDice; x++)
                        {
                            int curr = NextInt(1, roll.Die + 1);
                            fullRolls[i].Add(curr);
                        }
                        if (roll.Drop > 0)
                        {
                            msg += "{ [";
                            msg += string.Join("], [", fullRolls[i].Select(x => x.ToString()));
                            msg += "] } => ";
                        }
                        //drop the lowest if we must
                        //Could make this powers of 2 more efficient if I sort 
                        //and take (N - drop) rolls, but then the result is sorted
                        for (int x = 0; x < roll.Drop; x++)
                        {
                            int indx = 0;
                            for(int j = 1; j < fullRolls[i].Count; j++)
                            {
                                if (fullRolls[i][j] < fullRolls[i][indx])
                                    indx = j;
                            }

                            fullRolls[i].RemoveAt(indx);
                        }

                        msg += "{ [";
                        msg += string.Join("], [", fullRolls[i].Select(x => x.ToString()));
                        msg += "] }\n";
                    }

                    int rollMod = (roll.Mod ? -1 : 1) * roll.ModNum;
                    int[] rolls = fullRolls.Select(x => x == null ? -1 : x.Sum() + rollMod).ToArray();

                    msg += "= ";
                    if(roll.Adv == true)
                    {
                        msg += "{ [" + rolls[0] + "], [" + rolls[1] + "] } = **[" + Math.Max(rolls[0], rolls[1]) + "]**"; 
                    }
                    else if(roll.Adv == false)
                    {
                        msg += "{ [" + rolls[0] + "], [" + rolls[1] + "] } = **[" + Math.Min(rolls[0], rolls[1]) + "]**";
                    }
                    else
                    {
                        //no advantage/disadvantage
                        msg += "**[" + rolls[0] + "]**";
                    }
                }
                e.Channel.SendMessage("Rolling: *" + rollStr + ":*\n" + msg);

            }
        }

        //Dispose of the Bot
        public void Dispose()
        {
            //again, can I do this better?
            new Task(async () =>
            {
                //disconnect first
                await bot.Disconnect();
                //then dispose
                bot.Dispose();
                Console.WriteLine("We've ended!");
            }).RunSynchronously();
        }

        static void Main(string[] args)
        {
            try
            {
                Console.Write("Bot username: ");
                string usr = Console.ReadLine();
                Console.Write("Bot password: ");
                string psw = "";

                //read password without showing password on screen
                ConsoleKeyInfo key;
                for(;;)
                {
                    key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        break;
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (psw.Length > 0)
                        {
                            psw = psw.Substring(0, psw.Length - 1);
                            Console.Write("\b \b");
                        }
                    }
                    else
                    {
                        psw += key.KeyChar;
                        Console.Write("*");
                    }
                }

                Program prog = new Program(usr, psw);
                while (true)
                {
                    //I think a few microseconds every 2 weeks is enough!
                    Thread.Sleep(14 * 24 * 60 * 60 * 1000);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ReadLine();
            }

        }
    }
}
