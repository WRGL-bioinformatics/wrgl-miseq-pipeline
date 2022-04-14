# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels
or genotyping pipelines.

Installation and related setup details in WRGL SOP 032576 on iPassport.

## Version 3.0.0

## Change summary

* Switched default analysis platform to Iridis 5.
* Minor bugfixes
  * Updated interface to show messagebox on completion and if error
  * Downloads CNV report

### Patch 2021-07-01

 * To resolve samplesheet changes introduced by Local Run Manager.

### Patch 2021-05-27

 * To address crashing caused by failed samples following Pisces updates.
 * Win10 MiSeq update changes - now works across both sequencers.

### Bam-Download updated

 * Now transfers all BAM files (panels, genotyping, and myeloid) into a local temporary file store for ease of access by scientists with IGV

### Patch 2021-03-01

 * Removed FastqFileNames argument from Myeloid wrapper, as this was causing problems with Fastq files being read as a single string rather than dict<string, string>
 * TODO: GetFASTQFileNames function will be moved to ParseSampleSheet, and updated to handle all current fastq cases.

## Version 2.2.4

### Change summary
 * MINOR CHANGE: Added --skipCopyToNetwork flag to align with interface changes and speed up testing runs
 * MINOR CHANGE: Corrected some issues that were affecting automatic running by MiSeq Reporter.

## Version 2.2.3

### Change summary
 * MINOR CHANGE: Added target PreferredTranscripts and BCInterpretations files to the .ini config file

## Version 2.2.2

### Change summary
 * MINOR CHANGE: Used ThreadPool to limit number of concurrent genotyping analyses.
 * MINOR CHANGE: Added ForceDeleteDirectory to delete old runs including myeloid
 * MINOR CHANGE: Removed run completion/failure email calls as this is not currently working.

## Version 2.2.1

### Change summary
 * MINOR CHANGE: Downloads coverage .zip file automatically

NOTE: Decided that <major>.<minor>.<increment> versioning was probably the
simplest way to go.  

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
