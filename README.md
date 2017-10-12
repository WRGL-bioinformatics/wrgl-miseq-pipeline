# WRGL Pipeline

C# pipeline that runs on MiSeq completion and manages data through either panels
or genotyping pipelines.

## Version
v2.0 (DEV)

## Change summary

### network_running branch

A quick exploration of the feasability of running programs currently stored in
duplicate on the MiSeq C:\ drives from the network drive.

Sadly, doesn't seem to work for all tools - alignment, GATK, and variant calling
worked, but SNPEff froze and didn't complete after being left overnight.

### reduce_sleep branch

Reduce the sleep time before the pipeline checks for run completion.
Since BWA update, runs have been finishing faster so don't need to sleep as long
Changed to wait 3 hours.

### get_data branch

Add data download only functionality to the core pipeline, so we don't need to
have a separate edited instance of the pipeline (i.e. 2x the editing if we need
to update anything!).

## Installation instruction

The pipeline can be installed from the Z:\ drive, or built from source using 
Visual Studio and this repository. The pipeline is automatically triggered on
run completion by the MiSeq software, which looks in
C:\WRGLPipeline\WRGLPipeline\WRGLPipeline\bin\Release\
The executable and all DLLs should be copied into this folder, and should 
already by present if building from source.

Copy Z:\WRGLPipeline\Genotyping\human_g1k_v37 and 
Z:\WRGLPipeline\Genotyping\Software to the C:\WRGLPipeline folder.