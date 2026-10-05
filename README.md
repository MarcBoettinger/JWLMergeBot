# JWLMergeBot

## Introduction
🤖 [JWLMergeBot](https://t.me/JWLibraryMergeBot) is a Telegram bot meant to merge two [_JW Library_](https://jw.org) backup files into a single one, and to keep that backup healthy.

Maybe you are using _JW Library_ from your phone 📱, your computer 💻 and your tablet 📱.
But, since the data is not synchronized, you have to choose between:
* Keep your notes separated between devices (not so good😫)
* Backup and restore your most recent backup before using the app (but you regularly forget which device has the newest data 🤦‍♂️)

With this bot, you have to remember **nothing at all**. Just use the app. And when you remember to do it, simply send the backup to the bot from one of your devices 📩.
It will keep a repository of your notes, highlights, bookmarks, playlists... and it will merge your files as you send them to it.

## Features
* 🔗 **Merge** two backups into one, playlists included
* 🔍 **Merge preview**: see what will be added before anything is written, and choose which version wins for notes edited on both devices
* 🩺 **Health check**: find (and safely repair) duplicate or empty notes, broken links and other problems that can stop a restore
* 📊 **Study stats**: totals, notes per month, note streaks, words per note, Bible coverage, most highlighted chapters, top tags, highlight colours and milestones
* 🔎 **Search** your notes by words
* 🏷 **Tag manager**: list, rename, merge and clean up your tags
* ✂️ **Extract** a small backup: one tag, a service year or the last months
* ✂️ **Edit tools**: for example, remove all your favorites
* 🌍 English, German and Italian, switchable per chat

## Usage
* Start a Telegram chat with [@JWLibraryMergeBot](https://t.me/JWLibraryMergeBot)
* Send the first .jwlibrary file: the bot keeps it as your stored file
* Send the second .jwlibrary file
* Check the merge preview and confirm with **Merge**
* Get the merged .jwlibrary file and restore it with _JW Library_

### Merge preview
Before merging, the bot tells you what the merge would do, for example: *3 new notes, 5 new highlights, 2 notes edited on both devices*. Nothing is deleted by a merge, and nothing is written until you confirm. **Cancel** drops the new file and leaves the stored one untouched.

If some notes were edited on both devices, tap **Review conflicts** to see both versions side by side and pick, one by one, the stored or the new one. Without a choice, the most recent edit wins.

The preview can be switched off in **/settings**, in which case the bot merges straight away.

### Health check
Send **/health** to scan your stored file. It is read only: it reports what it found, sorted by importance (🔴 can stop a restore, 🟡 clutter, ⚪ just so you know). Tap **Repair my file** to fix the 🔴 and 🟡 items and get the repaired file back. If the bot can't repair the file safely, it changes nothing.

### Search
Send **/search** followed by one or more words, for example `/search love`. Every word must appear in the title, the text, a tag or the place of a note (upper and lower case don't matter). The bot lists the most recent matches with their scripture or publication.

### Tags
Send **/tags** to see your tags with the number of items each one carries. Rename one with `/renametag old > new`, merge two with `/mergetags source > target` (everything moves to the second tag and the first is removed), or delete the tags no note uses with the button under the list. These changes update your stored file, and the bot sends you the edited file.

### Extract
**/extract** cuts a small backup out of your stored file: the notes of one tag, of the current or the last service year, or of the last 6 or 12 months. The extract contains these notes with their tags and attached highlights, but no bookmarks or playlists. Your stored file is not changed. Periods use the date each note was last edited.

### Study stats
Send **/studystats** for a summary of your study. Highlights carry no date in a backup, so activity and streaks are based on your notes only.

### Commands
| Command | What it does |
|---|---|
| /start | Introduction |
| /fileinfo | Details about the stored file |
| /health | Scan the stored file for problems |
| /studystats | Statistics about your study |
| /search words | Find notes |
| /tags | Manage your tags |
| /extract | Make a small backup from a part of your file |
| /editfile | Edit tools |
| /settings | Language, delete after send, merge preview |
| /delete | Delete your stored file now |
| /botinfo | Version and credits |

## Credits
The bot is a fork of the [JWLMergeBot](https://gitlab.com/AlessandroLucchet/JWLMergeBot) of _Alessandro Lucchet_, enhanced with playlist support, the health check, study stats and the merge preview.
It is based on [JWLMerge](https://github.com/AntonyCorbett/JWLMerge) of _Antony Corbett_ and it uses [this .NET Client](https://github.com/TelegramBots/Telegram.Bot) to deal with the [Telegram Bot API](https://core.telegram.org/bots/api).
The health check and the study stats are inspired by the Library Doctor and the Study Stats of [JW Sync](https://jwsync.org).

*JW Library* is a registered trademark of _Watch Tower Bible and Tract Society of Pennsylvania_. This project is not affiliated with it.

## Disclaimer
The bot needs to process your backup files to work.\
So, when you send the first backup file, this file is stored on the server where the bot runs.\
When you send the second one, the bot merges it with the first one, and it keeps the result.\
If the merge preview is on, the second file waits on the server until you confirm or cancel (and is deleted after 24 hours at most).\
_Keep this in mind if you store sensitive information in your notes._
>Note: You can manually delete all your data with the **/delete** command. Or you can enable the option to delete all your data immediately after the merge with the **/autodelete_on** command.

If you don't want to send your backup to the JWLibraryMergeBot server, you can set up yours. Just follow the next section.

## Create your own JWLMergeBot

* Create your bot with [BotFather](https://t.me/botfather) and get the _bot token_.
* Clone this project with Visual Studio (or any editor, the project targets **.NET 8**)

### Building for Windows
* Compile the project
* Put the _bot token_ in the **config.json** file (it must be located in the same directory of the executable)
* Launch the bot or create a service with [`sc create`](https://docs.microsoft.com/it-it/windows-server/administration/windows-commands/sc-create)

### Building for [Raspbian](https://www.raspberrypi.org/downloads/raspbian/)

* Compile the project with this command: `dotnet publish -r linux-arm`
* Compile the SQLite library for the Raspberry. You have to:
	* Download [the full-source of the library](https://system.data.sqlite.org/index.html/doc/trunk/www/downloads.wiki) and then execute:

		```
		sudo apt-get update
		sudo apt-get install build-essential
		cd <source root>/Setup
		chmod +x compile-interop-assembly-release.sh
		./compile-interop-assembly-release.sh
		```
	* Copy the **libSQLite.Interop.so** and **SQLite.Interop.dll** files in your release folder
* Put the _bot token_ in the **config.json** file (it must be located in the same directory of the executable)
* Launch the bot or create a service with [`systemd`](https://devblogs.microsoft.com/dotnet/net-core-and-systemd/)
