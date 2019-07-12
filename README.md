# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels
or genotyping pipelines.

## Version 2.2

### Change summary
 * MINOR CHANGE: Update error handling for Iridis connection.
 * Should try A, B, then C login nodes. Currently fails after A.
 * MINOR CHANGE: Use aux0_Start_Pipeline.sh to trigger Iridis analysis

NOTE: version was meant to be 2.1a, as it was such a minor change, but it's 
stored in the program as a float so this was not possible.

## Version 2.1

### Change summary
 * Removed hard-coding of depth cutoffs. Instead do via .ini file
 Updated reporting module to handle uncertain ./. & ./1 genotypes

## Version 2.0

### Change summary
 * Moved software and references to Z: drive
 * Added -getdata option to run download only mode
 * Reduced Iridis wait time to 3 hours
 * integrated with graphical interface