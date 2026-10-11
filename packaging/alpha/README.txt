Stars of Darien, @VERSION@

WHAT THIS IS

Stars of Darien is a free remaster of Total Annihilation: Kingdoms, made by
fans. This is an early alpha for a small group of playtesters. Expect bugs,
rough edges and things that change from one build to the next.

This alpha has skirmish against the computer. Multiplayer and the campaign
come in later builds. KNOWN-ISSUES.txt lists what is not in yet and the
rough edges we already know about.

This game was called Darien Reforged until now. The first time it starts
under its new name, it copies your saved games, screenshots, options and
game folder setting from Darien Reforged, and it leaves those as they were.


YOU NEED YOUR OWN COPY OF THE GAME

Stars of Darien ships none of the original game's files. It reads them from
your own installed copy of Total Annihilation: Kingdoms, such as the GOG
edition or the original CD. Install that first if you have not.


STARTING THE GAME

1. Unzip the whole download somewhere you like, such as your Desktop or
   C:\Games. Keep everything in it together.

2. Open the "Stars of Darien" folder and double click "Stars of Darien.exe".

3. The first time, Windows may show a blue box that says "Windows protected
   your PC". This build is not signed yet, so Windows does not know the
   publisher. Click "More info", then "Run anyway". Windows remembers that.

4. The game looks for Total Annihilation: Kingdoms in the usual places. When
   it cannot find it, it asks you for the folder. Pick the folder the game is
   installed in, the one with data.hpi and terrain.hpi in it. The GOG edition
   installs to C:\GOG Games\Total Annihilation Kingdoms. Type the path, or
   press Browse to look through your drives, then press "Use this folder".
   The game remembers your choice.

5. To use another copy later, open Options from the main menu and press
   Change beside Game folder.


PLAYING

On the main menu, click the left door (Play the Machine) for a skirmish
against the computer. Pick a map, your kingdom and your opponents, then
start. F1 opens the game menu, and F9 saves a screenshot.

Builders can help finish a building that another builder has started.
Select them and point at its frame. With the Classic controls a hammer
shows over a frame they can help, and a left click sends them. With the
Modern controls, right click the frame. Each helper adds its own work and
pays its own share, so two builders finish in half the time for the same
mana. A builder with a fixed list, such as a Mage Builder, helps only with
what is on its list, while a monarch helps with anything.


BATTLES THAT BREAK

Every kind of blast now throws its own fire, smoke, dust and debris, and
leaves a scar or a crater that the ground keeps for the rest of the battle.
Trees, walls and buildings break apart in 3D, a dying unit's pieces fly
off it, scenery burns and the fire spreads, and each kind of magic marks
the scenery it reaches. Every stage that scenery breaks or burns into has
a 3D model now, the ruins of Creon's towns included.

Options has a Battle effects row under Display: Low, Medium, High or Ultra,
for how much smoke, debris, light and scarring a battle shows. Until you
pick one, the game uses the one that suits your graphics card. If battles
run slowly, try Low.


REMASTERED BATTLEFIELD RULES

Stars of Darien always plays the remastered battlefield rules, which go
beyond the original game's. There is no setting for them.

- Rocks, ruins, spires and grass can be destroyed too. Lodestones and
  sacred sites cannot.
- Walls and buildings leave rubble that blocks the way until a builder
  clears it with the Clear order.
- Fire spreads farther from one tree to the next, and it hurts units that
  stand in it.
- The computer players clear the rubble their units are stuck at and lead
  their units out of fire.


WHEN SOMETHING GOES WRONG

The game writes a log every time it runs, here:

    %USERPROFILE%\AppData\LocalLow\OpenKingdoms\Stars of Darien\Player.log

Paste that line into the address bar of File Explorer to open it. The log
of the run before is Player-prev.log in the same folder, and F9 screenshots
go to the Captures folder there.

When you report a bug, say what you did and what happened, and attach
Player.log, with a screenshot if you have one. Please copy the log before
you start the game again, since the next start replaces it.

If the main menu says the engine did not start, install the Microsoft Visual
C++ Redistributable (x64) from https://aka.ms/vs/17/release/vc_redist.x64.exe
and start the game again.


FEEDBACK

Bugs, ideas and anything else go to our Discord: [DISCORD LINK]


ABOUT

Stars of Darien is free software under the GNU General Public License,
version 3, with an extra permission for the Unity engine. See LICENSE.txt.
For three years from this release, ask in our Discord ([DISCORD LINK]) and
you'll get the source code.
The libraries and fonts it uses are listed with their licenses in
THIRD-PARTY-NOTICES.txt. Total Annihilation: Kingdoms and its files belong
to their owners, and this project is not made by or connected with them.
