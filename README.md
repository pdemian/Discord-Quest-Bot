# Discord Quest Bot
### A bot for Discord which allows you to play quests.

## QuestBot commands:  

### General commands: 
| Command                  | Description                      | Usage         |
| ------------------------ | -------------------------------- | ------------- |
| .help | Show this help text              | .help         |
| .rand &#91;Num1&#93;-&#91;Num2&#93; | Generate a random number between Num1 and Num2 | .rand 1-100 |
| .roll &#91;Num&#93;?d&#91;Num&#93;(+&#124;-&#91;Num&#93;)?&#91;A&#124;D&#93;? | Roll a die with optional modifiers and optional advantage/disadvantage | .roll 5d6 + 7 A | 

### Quest Commands: 
| Command                  | Description                      | Usage         |
| ------------------------ | -------------------------------- | ------------- |
| .startsession | Start a quest | .startsession |
| .endsession | End a quest | .endsession |
| .vote &#91;Number&#93; | Vote in a quest chapter | .vote 6 |
| .other &#91;String&#93; | Give another answer than currently votable | .other Maybe stealth is better | 
| .tally | Tally the current votes in this chapter | .tally |
| .next | Go to the next chapter in a quest | .next |

## Questing information: 

To start a session, the quest giver runs '.startsession'. 
He/she is now in control of ending the quest and moving to the next chapter. 
The quest giver writes out the current chapter of the quest and includes options to vote on with '.vote'. 
If none of the options seem appealing the quest giver may allow other answers to be posted with '.other'. 
A tally can be conducted once everyone has finished voting with '.tally' and the quest giver may move to the next chapter with '.next'.
This resets all votes and voting can start again. When the session is done, the quest giver can finish the quest by typing '.endsession'.
There can only be one session per channel at any time.