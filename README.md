# CK2ToEu4
A WIP C# console program that converts CK2 save games to EU4 mods

Designed for later use of my [EU4ToVic2](https://github.com/Dinglydell/EU4ToVic2) converter for grand campaigns. Notable features include:

* The splitting large culture blobs into smaller cultures in the same group (eg. German splitting into many cultures in the German group)
* Dynamic start date in EU4
* Lack of date-restricted features in EU4 - the idea is that it should be possible for the industrial revolution to be pulled back by several decades, or delayed
* Modified institution spread in EU4 - it's much more restricted so that the EU4ToVic2 converter can determine who should and should not be civilised
* Notifier for when the game ought to be converted to Vic2 (you can ignore it if you like)
* Dynamic, *deterministic* national ideas
* A lot of potential for customisation - most things the converter decides about your country are based on text files that determine what state of the CK2 realm creates what effect in your country. This includes national ideas
