# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels
or genotyping pipelines.

## Version
v2.1a

## Change summary
 * MINOR CHANGE: Update error handling for Iridis connection.
 * Should try A, B, then C login nodes. Currently fails after A.

## Old Version
v2.1

## Change summary
 * Removed hard-coding of depth cutoffs. Instead do via .ini file
 Updated reporting module to handle uncertain ./. & ./1 genotypes

## Old Version
v2.0

## Change summary
 * Moved software and references to Z: drive
 * Added -getdata option to run download only mode
 * Reduced Iridis wait time to 3 hours
 * integrated with graphical interface