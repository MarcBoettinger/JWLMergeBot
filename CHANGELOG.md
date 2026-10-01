## [1.10.0.19] - 2026-09-30

* Study stats: aligned bar columns with finer bars (monospace blocks), unambiguous dates (8 May 2017) and thousands separators
* Italian texts updated: playlists are supported, correct bot name in the welcome message, playlist count in the file info, typo fixed
* German added as a language (/settings, then Change language). Broadcast messages fall back to English when they have no text for a chat's language
* Merge preview: before merging, the bot shows what will be added (notes, highlights, bookmarks, tags, playlist items) and waits for confirmation. Can be switched off in /settings
* Conflict review: notes edited on both devices can be reviewed one by one, choosing the stored or the new version
* Fixed /botinfo: correct repository link and credits, no more bogus library version
* New commands: /health scans the stored file (like the Library Doctor on jwsync.org) and /health_fix repairs it
* Health check finds duplicate/empty notes, broken links, duplicate locations and other problems that can stop a restore

## [1.9.0.18] - 2026-05-02

* Support for the v16 db schema

## [1.8.1.17] - 2025-01-18

* Error handling while editing files

## [1.8.0.16] - 2023-10-20

* New command: edit file. The first available command is: "Delete favorites" to clear all favorites from file
* Fix: now the last modified date of the merged file is correctly filled

## [1.7.0.15] - 2023-08-16

* Support for the v14 db schema
* The bot can now send messages to users

## [1.6.2.14] - 2023-07-26

* Bugfix

## [1.6.1.13] - 2023-07-17

* Improved the handling of the file if db version doesn't match with supported one

## [1.6.0.12] - 2023-07-17

* Support for the v13 db schema
* Updated JWLMerge library (2.0.0.12)


## [1.5.0.11] - 2022-12-11

* Auto-ban chats if too many messages
* Updated JWLMerge library (2.0.0.12)

## [1.4.1.10] - 2022-04-17

* Better handling of bot disconnections

## [1.4.0.8] - 2022-01-16

* Updated Telegram.Bot library (17.0.0)
* Updated JWLMerge library (2.0.0.8)

## [1.3.1.7] - 2021-04-14

* Increased the number of retained log files
* Now the bot counts the number of merged files by user

## [1.3.0.6] - 2021-03-13

* Updated JWLMerge library (1.1.0.7)

## [1.2.2.5] - 2020-09-10

* Fixed wrong stats of merged file

## [1.2.1.4] - 2020-08-21

* Now the bot can handle stored files with old schema versions
* Updated Telegram.Bot library (15.7.1)
* Handling of a bug on the getFile method of Telegram API

## [1.2.0.3] - 2020-07-13

* Updated JWLMerge library (1.1.0.5)

## [1.1.0.2] - 2020-04-22

* Informations about file are now sent automatically

## [1.0.0.0] - 2020-04-18

* First release
