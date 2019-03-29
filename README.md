# Gibbed's Yakuza 0 Tools

Tools for modding the PC version of Yakuza 0.

Like what I've done? **[Consider supporting me on Patreon](http://patreon.com/gibbed)**.

[![Build status](https://ci.appveyor.com/api/projects/status/p3q4ml8567bxlvx3/branch/master?svg=true)](https://ci.appveyor.com/project/gibbed/gibbed-yakuza0/branch/master)

## What?

*Experimental!*

* Unpacker for `.par` archive files.
* Packer for `.par` archive files.
    * No support for compressing files yet.

## Instructions

[You can download binaries of the latest release](https://github.com/gibbed/Gibbed.Yakuza0/releases/latest) (not the source ZIP!).

## Notable Limitations with Archive Files

Some `.par` archive files have a hard size limit of how large they can be because the game (for some reason) loads them wholesale into memory. This is total size of the `.par` file itself, not the total uncompressed data contained within.

So for example, since compression is not implemented yet, when repacking an archive with compressed files, they will be repacked uncompressed, so the repacked archive will be much larger.

This list is not complete and is a result of investigation of repacking causing issues with Yakuza 0.

* `sprite_c.par`: 8954864 (`0x88A3F0`) bytes

## TODO

* Everything.
