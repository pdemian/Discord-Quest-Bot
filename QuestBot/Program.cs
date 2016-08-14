using System;
using System.Linq;
using System.Threading;
using Discord;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


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
        //For the following comments, example of a roll: 5d6 + 7 A

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
            dieRegex = new Regex(@"^(?<NumDice>\d{0,2}) *[dD] *(?<Die>\d{1,2}) *((?<Mod>[+-]) *(?<ModNum>\d{1,3}))? *(?<Adv>[AaDd])?", RegexOptions.Compiled);
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
            string msg = "QuestBot commands: \n" +
                         "General commands:" +
                         ".help                Show this help text\n" +
                         ".rand [Num1]-[Num2]  Generate a random number between Num1 and Num2\n" +
                         ".roll [Num]?d[Num](+|-[Num])?[A|D]?\n" +
                         "    Roll a die with modifiers and advantage/disadvantage\n" +
                         "\nQuest Commands:\n" +
                         ".startsession        Start a quest\n" +
                         ".endsession          End a quest\n" +
                         ".vote [Number]       Vote in a quest chapter\n" +
                         ".other [String]      Give another answer than currently votable\n" +
                         ".tally               Tally the current votes in this chapter\n" +
                         ".next                Go to the next chapter in a quest\n" +
                         "\nQuesting information:\n" +
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
            var randStr = e.Message.Text.Substring(6).Trim();

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
                e.Channel.SendMessage("Invalid roll. Expected [Num]?d[Num] (+|-[Num])? [A|D]?. Example '.roll d6' or '.roll 7d6 + 9 Advantage'");
                return;
            }

            //Match the string to the regex
            var rollStr = e.Message.Text.Substring(6).Trim();

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
                        bool emptyStr = !string.IsNullOrEmpty(g.Value);

                        if (name == "NumDice" && emptyStr)
                            roll.NumDice = int.Parse(g.Value);
                        else if (name == "Die")
                            roll.Die = int.Parse(g.Value);
                        else if (name == "Mod" && emptyStr)
                            roll.Mod = g.Value == "-" ? true : false;
                        else if (name == "ModNum" && emptyStr)
                            roll.ModNum = int.Parse(g.Value);
                        else if (name == "Adv" && emptyStr)
                            roll.Adv = g.Value.ToLower() == "a";
                    }
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
                    int[] rolls = { (roll.Mod ? -1 : 1) * roll.ModNum,
                                    (roll.Mod ? -1 : 1) * roll.ModNum };

                    //This whole section is ugly and it works!
                    //If we have to roll normally, only loops once, if we have to roll twice, it loops twice
                    for (int i = 0; i < 1 + (roll.Adv != null ? 1 : 0); i++)
                    {
                        msg += "{ [";
                        //generate X number of rolls, roll a number for that, add it to the current counter, 
                        //and join it together as a string
                        msg += string.Join("], [", Enumerable.Range(0, (int)roll.NumDice).Select(x => 
                        {
                            int curr = NextInt(1, roll.Die + 1);
                            rolls[i] += curr;
                            return curr.ToString();
                        } ).ToArray());
                        msg += "] }\n";
                    }


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
