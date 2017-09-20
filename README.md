# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels of genotyping pipelines.


# network_running branch

A quick exploration of the feasability of running programs currently stored in
duplicate on the MiSeq C:\ drives from the network drive.

Also want to look into centralising reference files (although larger, so this
may not be practical.)

# reduce_sleep branch

Reduce the sleep time before the pipeline checks for run completion.
Since BWA update, runs have been finishing faster so don't need to sleep as long
Changed to wait 3 hours.