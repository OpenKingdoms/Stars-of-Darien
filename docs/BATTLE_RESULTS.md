# Victory and defeat

What Stars of Darien shows when a skirmish is decided. It keeps the original's statistics screen, stat for stat and on its own art, and adds the pages a modern strategy game offers after a battle.

## The original, remastered

The moment the battle is decided the word Victory or Defeat stands over the battlefield in large gilded letters, centred over the play area as the original's VictoryText.gui and DefeatText.gui place it. The battle holds behind it. The music plays on, as it does in the original, which has no sound of its own for the verdict.

Three seconds later, the original's 90 frames, the statistics page fades in and the banner fades out. The page is the original's own screen: victory<side>.gui for the player's kingdom, or defeat.gui. Its painting comes from the player's files (VictoryBG in TAKV<side>Screen.gaf, DefeatBG in takdefeatscreen.gaf) and is drawn sharp at any size, scaled like every remastered menu page, on vellum with knotwork down the margins a wide screen leaves.

The first page is the original's table, with a row per kingdom: its badge from the original's teamlogos.gaf in the kingdom's colour, Player, Units, Kills, Losses, Time and Score, in the columns victory<side>.gui gives them. The numbers are the ones the original keeps. Units counts every unit and building begun, walls aside. Kills and Losses count units destroyed. Time is the last moment the kingdom stood, and Score adds up the experience value of every unit it killed. A line under the table says how the battle ended, on which map and after how long.

The two buttons stand where the original's do, on its art. Main Menu, the skull on the left, is Return to menu, and Proceed, the sword on the right, is Look at the field. Enter presses Proceed and Escape presses Main Menu, as the dialog's own accelerator line says, and the help strip at the foot names the button under the pointer.

Look at the field works as it did. It puts the page away and shows the whole map, and after a defeat the computers still at war fight on. The Results plate where the sidebar's menu button sits brings the page back, with the numbers as they stand by then.

## More and better

A row of four tabs along the top of the page picks what the page shows: Tallies, Graphs, Kingdoms and Annals. Tallies is the original's table over the original's painting. The other three unroll a dark panel over the painting, and the title and the buttons stay where they are.

Graphs draws one line a kingdom, in the kingdom's colour, over the whole battle. Six graphs are offered: Army is the units in the field, Worth what they cost in mana with the monarch left out, since nobody buys one, Income the mana gathered each second, Mana the pool, Kills the running count, and Lodestones how many each kingdom held. A monarch slain and a kingdom fallen are marked on the time line, and with the pointer over the graph the help strip reads every kingdom's figure at that moment.

Kingdoms shows one kingdom at a time, picked by its badge, with the player's own first. It gives units trained and buildings raised, kills and losses, damage dealt and taken, spells cast, mana gathered and spent, the most lodestones held at once and the largest army. Beside them are the kinds it made most, with their pictures from the game, and its champion: the unit with the most kills, its rank, and whether it still stands.

Annals lists the key moments with their times: the first blood, each monarch slain, each kingdom fallen or yielded, and the verdict. Beside them are six honours, each to the kingdom that earned it: Warlord for the most kills, Master builder for the most units built, Treasurer for the most mana gathered, Unbroken for the fewest losses among the kingdoms still standing, First blood, and Champion for the kingdom whose unit killed the most.

Left out on purpose: actions a minute, which say little about a game paced like this one, upgrades, which the game does not have, and a table of every kind for every kingdom, which would not read on one page.

## Where the numbers come from

The engine keeps them. The original's tallies are the world's PlayerBattleStats, as the classic end screen reads them. Beside them a battle record counts what the original never kept: finished units and buildings by kind, damage dealt and taken, spells cast (shots that cost mana), each kingdom's champion and the key moments. The economy keeps a running total of mana gathered and spent. Every 5 seconds of game time the record samples each kingdom's army, its worth, the pool, the totals, kills and lodestones held. When a long battle fills 512 samples it keeps every other one and samples half as often, so a battle of any length fits.

Nothing in the simulation reads the record. It stays out of the state hash, so it cannot set two machines in a match apart, and the pinned simulation probe does not move. It travels in a save in a section of its own, which an older engine skips.

The game reads it through the embedding API (version 23, which new calls do not bump): okx_battle_stats for a kingdom's numbers, okx_battle_series for a graph, okx_battle_built for the kinds it made, okx_battle_events for the key moments. okx_unit_record gives a living unit's kills and rank, which also fills the HUD's kill count and rank shield.

## Layout

The page is the original's 640 by 480, scaled to the screen's height, so it reads the same from 1280 by 720 to 3840 by 2160. Text never falls below 12 pixels at 1280 by 720. Eight kingdoms fit every page: the table has the original's eight rows, the graph legend wraps to two lines, and the Kingdoms page shows one kingdom at a time.
