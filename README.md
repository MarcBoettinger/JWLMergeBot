# JWLMergeBot 

## Introduction
🤖 [JWLMergeBot](https://t.me/JWLMergeBot) is a Telegram bot meant to merge two [_JW Library_](https://jw.org) backup file into a single one.

Maybe you are using _JW Library_ from your phone 📱, your computer 💻 and your tablet 📱.
But, since the data is not synchronized, you have to choose beetween:
* Keep your notes separated between devices (not so good😫)
* Backup and restore your most recent backup before to use the app (but you regularly forget which device has the newest data 🤦‍♂️)

With this bot, you have to remember **nothing at all**. Just use the app. And when you remeber to do it, simply send the backup to the bot from one of your device 📩.
It will keep a repository of your notes, highlights, bookmarks, ... and it will merge your files as you send them to it.

## Credits
The bot is based on [this project](https://github.com/AntonyCorbett/JWLMerge) of _Antony Corbett_ and it uses [this .NET Client](https://github.com/TelegramBots/Telegram.Bot) to deal with the [Telegram Bot API](https://core.telegram.org/bots/api).

*JW Library* is a registered trademark of _Watch Tower Bible and Tract Society of Pennsylvania_.

## Usage
* Start a Telegram chat with [@JWLMergeBot](https://t.me/JWLMergeBot)
* Send the first .jwlibrary file
* Send the second .jwlibrary file
* Get the merged .jwlibrary file and restore it with _JW Library_

## Limitations

Currently, the bot **doesn't support the playlists merge**.\
If you send a backup with a playlist, the merged file will not contains the playlists anymore.

## Disclaimer
The bot needs to process your backup files to works.\
So, when you send the first backup file, this file is kept in the memory of a server.\
When you send the second one, the bot merge it with the first one, and it keeps in memory the result.\
_Keep this in mind if you store sensitive informations in your notes._
>Note: You can manually delete all your data with the **/delete** command. Or you can enable the option to delete all your data immediately after the merge with the **/autodelete_on** command.

If you don't want to send your backup to the JWLMergeBot server, you can set up yours. Just follow the next section.



## Create your own JWLMergeBot

* Create your bot with [BotFather](https://t.me/botfather) and get the _bot token_.
* Clone this project with Visual Studio

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
	* Copy the **libSQLite<span>.Interop.so** and **libSQLite<span>.Interop.so** files in your release folder
* Put the _bot token_ in the **config.json** file (it must be located in the same directory of the executable)
* Launch the bot or create a service with [`systemd`](https://devblogs.microsoft.com/dotnet/net-core-and-systemd/)
