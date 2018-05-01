# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels
or genotyping pipelines.

## Version
v2.1

## Change summary
 * TO TEST: Remove hard-coding of depth cutoffs. Instead do via .ini file
 * TODO: Update reporting module to handle uncertain ./. & ./1 genotypes
 * TODO: Exome specific processing - download and use BED file from Iridis

## Old Version
v2.0

## Change summary
 * Moved software and references to Z: drive
 * Added -getdata option to run download only mode
 * Reduced Iridis wait time to 3 hours
 * integrated with graphical interface