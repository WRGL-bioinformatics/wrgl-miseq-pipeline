using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace WRGLPipeline
{
    class GenerateGenotypingVCFs
    {
        SampleRecord record;
        readonly string analysisDir;
        readonly ProgrammeParameters parameters;
        readonly ParseSampleSheet sampleSheet;
        readonly Tuple<string, string> fastqFileNames;

        /// <summary>
        /// Runs the external programmes needed to perform genotyping analysis.
        /// Constructor initialises the object, but the functions to actually run it are called directly
        /// by GenotypingPipelineWrapper. Not sure if this is entirely needed for all cases, but certainly
        /// variant calling is done at the run level (so maybe this needs to be split into *two* classes?
        /// </summary>
        /// <param name="_record">Details of the sample to analyse</param>
        /// <param name="_analysisDir">Analysis folder</param>
        /// <param name="_parameters">Configured ProgrammeParameters</param>
        /// <param name="_sampleSheet">Parsed SampleSheet</param>
        /// <param name="_fastqFilenames">R1 and R2 fastq filenames for the sample</param>
        public GenerateGenotypingVCFs(SampleRecord _record, string _analysisDir, ProgrammeParameters _parameters, ParseSampleSheet _sampleSheet, Tuple<string, string> _fastqFilenames)
        {
            this.record = _record;
            this.analysisDir = _analysisDir;
            this.parameters = _parameters;
            this.sampleSheet = _sampleSheet;
            this.fastqFileNames = _fastqFilenames;
        }

        /// <summary>
        /// Use AmpliconAligner to align against the reference file - runs at a SAMPLE level
        /// </summary>
        /// <remarks>Could be altered to irectly take the file names, so could be tested more easily?</remarks>
        public void MapReads()
        {
            StringBuilder alignmentParameters = new StringBuilder();
            // update samtools to run a single command - sort can also output BAM
            StringBuilder samtoolsSortBamParameters = new StringBuilder();
            StringBuilder samtoolsIndexBamParameters = new StringBuilder();
            StringBuilder realignerTargetCreatorParameters = new StringBuilder();
            StringBuilder indelRealignerParameters = new StringBuilder();
            // For testing - gemini indel realigner handles end-of-amplicon variants better than gatk,
            //               and reduces abberant indel calls in genotyping.
            StringBuilder geminiIndelRealignerParameters = new StringBuilder();

            StreamWriter logFile = new StreamWriter(analysisDir + @"\" + record.Sample_ID + @"_genotyping.log");

            alignmentParameters.Append(sampleSheet.Analyses["G"]);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item1);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item2);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(analysisDir);
            alignmentParameters.Append(@"\");
            alignmentParameters.Append(record.Sample_ID);

            // Updated to convert SAM > BAM with the sort command
            samtoolsSortBamParameters.Append(@"sort ");
            samtoolsSortBamParameters.Append(@"-O BAM ");
            samtoolsSortBamParameters.Append(@"-o ");
            samtoolsSortBamParameters.Append(analysisDir);
            samtoolsSortBamParameters.Append(@"\");
            samtoolsSortBamParameters.Append(record.Sample_ID);
            samtoolsSortBamParameters.Append(@"_sorted.bam ");
            samtoolsSortBamParameters.Append(analysisDir);
            samtoolsSortBamParameters.Append(@"\");
            samtoolsSortBamParameters.Append(record.Sample_ID);
            samtoolsSortBamParameters.Append(@".sam");

            samtoolsIndexBamParameters.Append(@"index ");
            samtoolsIndexBamParameters.Append(analysisDir);
            samtoolsIndexBamParameters.Append(@"\");
            samtoolsIndexBamParameters.Append(record.Sample_ID);
            samtoolsIndexBamParameters.Append(@"_sorted.bam ");
            samtoolsIndexBamParameters.Append(analysisDir);
            samtoolsIndexBamParameters.Append(@"\");
            samtoolsIndexBamParameters.Append(record.Sample_ID);
            samtoolsIndexBamParameters.Append(@"_sorted.bai");

            realignerTargetCreatorParameters.Append(@"-Xmx2g ");
            realignerTargetCreatorParameters.Append(@"-jar ");
            realignerTargetCreatorParameters.Append(parameters.GatkPath);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-T RealignerTargetCreator ");
            realignerTargetCreatorParameters.Append(@"-R ");
            realignerTargetCreatorParameters.Append(parameters.GenotypingReferenceFastaPath);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-I ");
            realignerTargetCreatorParameters.Append(analysisDir);
            realignerTargetCreatorParameters.Append(@"\");
            realignerTargetCreatorParameters.Append(record.Sample_ID);
            realignerTargetCreatorParameters.Append(@"_sorted.bam ");
            realignerTargetCreatorParameters.Append(@"-o ");
            realignerTargetCreatorParameters.Append(analysisDir);
            realignerTargetCreatorParameters.Append(@"\");
            realignerTargetCreatorParameters.Append(record.Sample_ID);
            realignerTargetCreatorParameters.Append(@"_RTC.intervals ");
            realignerTargetCreatorParameters.Append(@"-dt NONE ");
            realignerTargetCreatorParameters.Append(@"-known ");
            realignerTargetCreatorParameters.Append(parameters.KnownIndels1Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-known ");
            realignerTargetCreatorParameters.Append(parameters.KnownIndels2Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-known ");
            realignerTargetCreatorParameters.Append(parameters.KnownIndels3Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-ip 100 ");
            realignerTargetCreatorParameters.Append(@"-L ");
            realignerTargetCreatorParameters.Append(analysisDir);
            realignerTargetCreatorParameters.Append(@"\GenotypingRegions.bed ");
            realignerTargetCreatorParameters.Append(@"-et NO_ET ");
            realignerTargetCreatorParameters.Append(@"-K ");
            realignerTargetCreatorParameters.Append(parameters.GatkKeyPath);

            indelRealignerParameters.Append(@"-Xmx2g -jar ");
            indelRealignerParameters.Append(parameters.GatkPath);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-T IndelRealigner ");
            indelRealignerParameters.Append(@"-R ");
            indelRealignerParameters.Append(parameters.GenotypingReferenceFastaPath);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-I ");
            indelRealignerParameters.Append(analysisDir);
            indelRealignerParameters.Append(@"\");
            indelRealignerParameters.Append(record.Sample_ID);
            indelRealignerParameters.Append(@"_sorted.bam ");
            indelRealignerParameters.Append(@"-targetIntervals ");
            indelRealignerParameters.Append(analysisDir);
            indelRealignerParameters.Append(@"\");
            indelRealignerParameters.Append(record.Sample_ID);
            indelRealignerParameters.Append(@"_RTC.intervals ");
            indelRealignerParameters.Append(@"-o ");
            indelRealignerParameters.Append(analysisDir);
            indelRealignerParameters.Append(@"\");
            indelRealignerParameters.Append(record.Sample_ID);
            indelRealignerParameters.Append(@".bam ");
            indelRealignerParameters.Append(@"-known ");
            indelRealignerParameters.Append(parameters.KnownIndels1Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-known ");
            indelRealignerParameters.Append(parameters.KnownIndels2Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-known ");
            indelRealignerParameters.Append(parameters.KnownIndels3Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-dt NONE ");
            indelRealignerParameters.Append(@"--maxConsensuses 300000 ");
            indelRealignerParameters.Append(@"--maxReadsForConsensuses 1200000 ");
            indelRealignerParameters.Append(@"--maxReadsForRealignment 200000000 ");
            indelRealignerParameters.Append(@"--LODThresholdForCleaning 0.4 ");
            indelRealignerParameters.Append(@"-et NO_ET ");
            indelRealignerParameters.Append(@"-K ");
            indelRealignerParameters.Append(parameters.GatkKeyPath);

            // Run 4 threads at a time, but review this setting depending on real-world testing results
            geminiIndelRealignerParameters.Append(parameters.GeminiMultiPath);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append(@"--numprocesses " + parameters.GeminiMultiThreads);
            geminiIndelRealignerParameters.Append(@" ");
            // Stops time-consuming analysis of additional contigs. May need editing or removing for GRCh38
            geminiIndelRealignerParameters.Append(@"--chromosomes 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,X,Y ");
            geminiIndelRealignerParameters.Append(@"--samtools ");
            geminiIndelRealignerParameters.Append(parameters.SamtoolsPath);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append(@"--exepath ");
            geminiIndelRealignerParameters.Append(parameters.GeminiPath);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append(@"--genome ");
            geminiIndelRealignerParameters.Append(parameters.GenotypingReferenceFolderPath);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append(@"--outfolder ");
            geminiIndelRealignerParameters.Append(analysisDir + @"\" + record.Sample_ID);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append(@"--bam ");
            geminiIndelRealignerParameters.Append(analysisDir + @"\" + record.Sample_ID);
            geminiIndelRealignerParameters.Append(@"_sorted.bam");

            //align reads
            Process ampliconAlignerV2 = new Process();
            ampliconAlignerV2.StartInfo.FileName = parameters.AmpliconAlignerPath;
            ampliconAlignerV2.StartInfo.Arguments = alignmentParameters.ToString();
            ampliconAlignerV2.StartInfo.UseShellExecute = false;
            ampliconAlignerV2.StartInfo.RedirectStandardOutput = true;
            ampliconAlignerV2.StartInfo.RedirectStandardError = true;

            ampliconAlignerV2.Start();

            logFile.Write(ampliconAlignerV2.StandardOutput.ReadToEnd());
            logFile.Write(ampliconAlignerV2.StandardError.ReadToEnd());

            ampliconAlignerV2.WaitForExit();
            ampliconAlignerV2.Close();

            //sort SAM and convert to BAM
            Process samtoolsSortBam = new Process();
            samtoolsSortBam.StartInfo.FileName = parameters.SamtoolsPath;
            samtoolsSortBam.StartInfo.Arguments = samtoolsSortBamParameters.ToString();
            samtoolsSortBam.StartInfo.UseShellExecute = false;
            samtoolsSortBam.StartInfo.RedirectStandardOutput = true;
            samtoolsSortBam.StartInfo.RedirectStandardError = true;
            
            samtoolsSortBam.Start();
            
            logFile.Write(samtoolsSortBam.StandardOutput.ReadToEnd());
            logFile.Write(samtoolsSortBam.StandardError.ReadToEnd());

            samtoolsSortBam.WaitForExit();
            samtoolsSortBam.Close();

            //index BAM
            Process samtoolsIndexBam = new Process();
            samtoolsIndexBam.StartInfo.FileName = parameters.SamtoolsPath;
            samtoolsIndexBam.StartInfo.Arguments = samtoolsIndexBamParameters.ToString();
            samtoolsIndexBam.StartInfo.UseShellExecute = false;
            samtoolsIndexBam.StartInfo.RedirectStandardOutput = true;
            samtoolsIndexBam.StartInfo.RedirectStandardError = true;
            
            samtoolsIndexBam.Start();
           
            logFile.Write(samtoolsIndexBam.StandardOutput.ReadToEnd());
            logFile.Write(samtoolsIndexBam.StandardError.ReadToEnd());

            samtoolsIndexBam.WaitForExit();
            samtoolsIndexBam.Close();

            // Check to see what indel realigner to use
            if (parameters.GenotypingUseGemini == true)
            {
                Process geminiIndelRealigner = new Process();
                geminiIndelRealigner.StartInfo.FileName = "dotnet";
                geminiIndelRealigner.StartInfo.Arguments = geminiIndelRealignerParameters.ToString();
                geminiIndelRealigner.StartInfo.UseShellExecute = false;
                geminiIndelRealigner.StartInfo.RedirectStandardOutput = true;
                geminiIndelRealigner.StartInfo.RedirectStandardError = true;
                
                geminiIndelRealigner.Start();
                
                logFile.Write(geminiIndelRealigner.StandardOutput.ReadToEnd());
                logFile.Write(geminiIndelRealigner.StandardError.ReadToEnd());

                geminiIndelRealigner.WaitForExit();
                geminiIndelRealigner.Close();

                // Move files from gemini analysis directory up to the main run folder
                File.Copy(analysisDir + @"\" + record.Sample_ID + @"\" + record.Sample_ID + "_sorted.PairRealigned.bam", analysisDir + @"\" + record.Sample_ID + @".bam");
                File.Copy(analysisDir + @"\" + record.Sample_ID + @"\" + record.Sample_ID + "_sorted.PairRealigned.bam.bai", analysisDir + @"\" + record.Sample_ID + @".bam.bai");
                // Delete the temporary Gemini analysis folder
                FileManagement.ForceDeleteDirectory(analysisDir + @"\" + record.Sample_ID);
            }
            // if not told to use Gemini, fall back on Gatk IndelRealigner
            else
            {
                //create regions file to run realigner over
                Process realignerTargetCreator = new Process();
                realignerTargetCreator.StartInfo.FileName = parameters.JavaPath;
                realignerTargetCreator.StartInfo.Arguments = realignerTargetCreatorParameters.ToString();
                realignerTargetCreator.StartInfo.UseShellExecute = false;
                realignerTargetCreator.StartInfo.RedirectStandardOutput = true;
                realignerTargetCreator.StartInfo.RedirectStandardError = true;
                
                realignerTargetCreator.Start();
                
                logFile.Write(realignerTargetCreator.StandardOutput.ReadToEnd());
                logFile.Write(realignerTargetCreator.StandardError.ReadToEnd());

                realignerTargetCreator.WaitForExit();
                realignerTargetCreator.Close();

                //realign over intervals
                Process indelRealigner = new Process();
                indelRealigner.StartInfo.FileName = parameters.JavaPath;
                indelRealigner.StartInfo.Arguments = indelRealignerParameters.ToString();
                indelRealigner.StartInfo.UseShellExecute = false;
                indelRealigner.StartInfo.RedirectStandardOutput = true;
                indelRealigner.StartInfo.RedirectStandardError = true;
                
                indelRealigner.Start();
                
                logFile.Write(indelRealigner.StandardOutput.ReadToEnd());
                logFile.Write(indelRealigner.StandardError.ReadToEnd());

                indelRealigner.WaitForExit();
                indelRealigner.Close();

                logFile.Close();

                // Rename the index file from .bai to .bam.bai (just for consistency with existing data)
                File.Move(analysisDir + @"\" + record.Sample_ID + @".bai", analysisDir + @"\" + record.Sample_ID + @".bam.bai");
            }

            //cleanup files
            File.Delete(analysisDir + @"\" + record.Sample_ID + @".sam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_unsorted.bam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_sorted.bam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_sorted.bai");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_RTC.intervals");
        }

        /// <summary>
        /// Run the variant caller - runs at a RUN level
        /// </summary>
        /// <param name="analysisDir">Run analysis directory</param>
        /// <param name="parameters">Configred ProgrammeParameters</param>
        public static void CallSomaticVariants(string analysisDir, ProgrammeParameters parameters)
        {
            StringBuilder somaticVariantCallerParameter = new StringBuilder();
            // Pisces is an updated development of SVC
            StringBuilder piscesVariantCallerParameters = new StringBuilder();

            somaticVariantCallerParameter.Append(@"-B ");
            somaticVariantCallerParameter.Append(analysisDir);
            somaticVariantCallerParameter.Append(@" ");
            somaticVariantCallerParameter.Append(@"-g ");
            somaticVariantCallerParameter.Append(parameters.GenotypingReferenceFolderPath);
            somaticVariantCallerParameter.Append(@" ");
            somaticVariantCallerParameter.Append(@"-t 4 ");
            somaticVariantCallerParameter.Append(@"-f 0.01 ");
            somaticVariantCallerParameter.Append(@"-fo false ");
            somaticVariantCallerParameter.Append(@"-b 20 ");
            somaticVariantCallerParameter.Append(@"-q 100 ");
            somaticVariantCallerParameter.Append(@"-c 0 ");
            somaticVariantCallerParameter.Append(@"-a 20 ");
            somaticVariantCallerParameter.Append(@"-F 20 ");
            somaticVariantCallerParameter.Append(@"-gVCF false ");
            somaticVariantCallerParameter.Append(@"-i false ");
            somaticVariantCallerParameter.Append(@"-r ");
            somaticVariantCallerParameter.Append(analysisDir);
            somaticVariantCallerParameter.Append(@" ");
            somaticVariantCallerParameter.Append(@"-m 0");

            piscesVariantCallerParameters.Append(parameters.PiscesPath);
            piscesVariantCallerParameters.Append(@" ");
            piscesVariantCallerParameters.Append(@"--bam ");
            piscesVariantCallerParameters.Append(analysisDir);
            piscesVariantCallerParameters.Append(@" ");
            piscesVariantCallerParameters.Append(@"--genomefolders ");
            piscesVariantCallerParameters.Append(parameters.GenotypingReferenceFolderPath);
            piscesVariantCallerParameters.Append(@" ");
            // DEV: try 0.01 instead of 0.1 for lowest variant call, since we have very high depth 
            piscesVariantCallerParameters.Append(@"--minvf 0.01 ");
            piscesVariantCallerParameters.Append(@"--minbq ");
            piscesVariantCallerParameters.Append(parameters.GenotypingQual);
            piscesVariantCallerParameters.Append(@" ");
            piscesVariantCallerParameters.Append(@"--minmq ");
            piscesVariantCallerParameters.Append(parameters.GenotypingQual);
            piscesVariantCallerParameters.Append(@" ");
            piscesVariantCallerParameters.Append(@"--minvq 20 ");
            piscesVariantCallerParameters.Append(@"--maxvq 100 ");
            // This was set according the the SVC parameters, but 20 is apparently outisde the range for Pisces,
            // which has a minimum of 1000.
            piscesVariantCallerParameters.Append(@"--mindpfilter 1000 ");
            // DEV: trying this out at the same depth as the genotyping FAIL threshold
            // TODO: If keeping this setting, get the min depth from the params.
            piscesVariantCallerParameters.Append(@"--mindp ");
            piscesVariantCallerParameters.Append(parameters.GenotypingDepth);
            piscesVariantCallerParameters.Append(@" ");
            piscesVariantCallerParameters.Append(@"--gvcf false ");
            piscesVariantCallerParameters.Append(@"--ssfilter false");

            // If the Pisces flag is set in the ini file, use it as the variant caller
            // otherwise use the SomaticVariantCaller
            if (parameters.GenotypingUsePisces == true)
            {
                Process callPiscesVariants = new Process();
                callPiscesVariants.StartInfo.FileName = "dotnet";
                callPiscesVariants.StartInfo.Arguments = piscesVariantCallerParameters.ToString();
                callPiscesVariants.StartInfo.UseShellExecute = false;
                try
                {
                    callPiscesVariants.Start();
                }
                catch
                {
                    AuxillaryFunctions.WriteLog("Could not start Pisces variant caller.", parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }

                callPiscesVariants.WaitForExit();
                callPiscesVariants.Close();
            }
            else
            {
                Process callSomaticVariants = new Process();
                callSomaticVariants.StartInfo.FileName = parameters.SomaticVariantCallerPath;
                callSomaticVariants.StartInfo.Arguments = somaticVariantCallerParameter.ToString();
                callSomaticVariants.StartInfo.UseShellExecute = false;
                try
                {
                    callSomaticVariants.Start();
                }
                catch
                {
                    AuxillaryFunctions.WriteLog("Could not start Somatic Variant Caller.", parameters.LocalLogFilename, -1, false, parameters);
                    throw;
                }

                callSomaticVariants.WaitForExit();
                callSomaticVariants.Close();
            }
        }

        /// <summary>
        /// Creates a VCF file with all unique variants across all samples
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="sampleSheet">Parsed SampleSheet</param>
        /// <param name="analysisDir">Analysis folder of run</param>
        public static void CompressVariants(ProgrammeParameters parameters, ParseSampleSheet sampleSheet, string analysisDir) //create unique list of variant passing QC for annotation
        {
            AuxillaryFunctions.WriteLog(@"Compressing variants for annotation...", parameters.LocalLogFilename, 0, false, parameters);

            StreamWriter compressedUnAnnotatedVariantsFile = new StreamWriter(analysisDir + @"\UnannotatedVariants.vcf");
            //GenomicVariant tempVariant;
            HashSet<GenomicVariant> uniqueGenomicVariants = new HashSet<GenomicVariant>();

            //write headers to UnannotatedVariants VCF file
            compressedUnAnnotatedVariantsFile.Write("##fileformat=VCFv4.1\n");
            compressedUnAnnotatedVariantsFile.Write("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\n");

            //loop over VCF files
            foreach (SampleRecord sampleRecord in sampleSheet.SampleRecords)
            {
                if (sampleRecord.Analysis != "G")
                {
                    continue;
                }

                //parse VCF and bank entries
                //if (parameters.getGenotypingUsePisces == true)
                //{
                //    ParseVCF parseVCFFile = new ParseVCF(analysisDir + @"\" + sampleRecord.Sample_ID + "_S999.vcf", logFilename, parameters);
                //}
                //else
                //{
                //    ParseVCF parseVCFFile = new ParseVCF(analysisDir + @"\" + sampleRecord.Sample_ID + "_S999.vcf", logFilename, parameters);
                // }
                // DEV: To account for using Pisces, which doesn't add _S999.vcf (while still allowing use of SVC, which does), see if
                //      we can select the vcf file with a wildcard to allow both options (otherwise will need to use switch above, which
                //      was causing issues with VS thinking that parseVCFFile was undefined.
                //  NOTE: wildcards don't work, so try the GetFiles method instead.
                string vcffile = Directory.GetFiles(analysisDir, sampleRecord.Sample_ID + @"*.vcf")[0];
                ParseVCF parseVCFFile = new ParseVCF(vcffile, parameters);
                //loop over VCF entries
                // SVC and Pisces assign different values to the sample ID in the VCF
                // This is very annoying, but I can't think of a simple way around it...
                string sampleid = "SampleID";
                if (parameters.GenotypingUsePisces == true)
                {
                    sampleid = sampleRecord.Sample_ID + @".bam"; 
                }
                
                foreach (VCFRecordWithGenotype record in parseVCFFile.VCFRecords[sampleid])
                {
                    //store variants that pass qc
                    if (record.FILTER == "PASS" && record.QUAL >= parameters.GenotypingQual && int.Parse(record.INFO["DP"]) >= parameters.GenotypingDepth)
                    {
                        GenomicVariant tempVariant = new GenomicVariant(CHROM: record.CHROM, REF: record.REF, ALT: record.ALT, POS: record.POS);
                        uniqueGenomicVariants.Add(tempVariant);
                    }
                }

            } //done looping over files

            foreach (GenomicVariant variant in uniqueGenomicVariants)
            {
                StringBuilder line = new StringBuilder();

                line.Append(variant.CHROM);
                line.Append("\t");
                line.Append(variant.POS);
                line.Append("\t");
                line.Append(@".");
                line.Append("\t");
                line.Append(variant.REF);
                line.Append("\t");
                line.Append(variant.ALT);
                line.Append("\t");
                line.Append(@".");
                line.Append("\t");
                line.Append(@".");
                line.Append("\t");
                line.Append(@".");
                line.Append("\n");

                compressedUnAnnotatedVariantsFile.Write(line.ToString());
                line.Clear();
            }

            compressedUnAnnotatedVariantsFile.Close();
        }

        /// <summary>
        /// Runs SNPEff to annotated the unique VCF
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="analysisDir">Analysis folder of run</param>
        /// <returns>ParseVCF representation of the annotated VCF file</returns>
        public static ParseVCF CallSNPEff(ProgrammeParameters parameters, string analysisDir)
        {
            AuxillaryFunctions.WriteLog(@"Calling SnpEff annotator...", parameters.LocalLogFilename, 0, false, parameters);
            StreamWriter annotatedVCF = new StreamWriter(analysisDir + @"\AnnotatedVariants.vcf");
            StringBuilder snpEffParameters = new StringBuilder();

            //build snpEff parameters
            snpEffParameters.Append(@"-Xmx4g -jar ");
            snpEffParameters.Append(parameters.SnpEffPath);
            snpEffParameters.Append(" ");
            snpEffParameters.Append(@"-v GRCh37.75 ");
            snpEffParameters.Append(@"-noStats ");
            snpEffParameters.Append(@"-no-downstream ");
            snpEffParameters.Append(@"-no-intergenic ");
            snpEffParameters.Append(@"-no-upstream ");
            snpEffParameters.Append(@"-no INTRAGENIC ");
            snpEffParameters.Append(@"-spliceSiteSize 10 ");
            snpEffParameters.Append(@"-onlyTr ");
            snpEffParameters.Append(parameters.PreferredTranscriptsPath);
            snpEffParameters.Append(@" ");
            snpEffParameters.Append(@"-noLog "); //sends data back to snpEff; disabled
            snpEffParameters.Append(@"-formatEff "); // for newer SNPEff versions, enables older EFF= format annotations that are pipeline-compatible.
            snpEffParameters.Append(analysisDir);
            snpEffParameters.Append(@"\UnannotatedVariants.vcf");

            // DEV
            Console.WriteLine($@"Java path is {parameters.JavaPath}");
            Console.WriteLine($@"Pref transcripts file {parameters.PreferredTranscriptsPath}");
            Console.WriteLine($@"Analysis dir: {analysisDir}");

            //annotated variants
            ProcessStartInfo annotateSnpEff = new ProcessStartInfo
            {
                FileName = parameters.JavaPath,
                Arguments = snpEffParameters.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false
            };

            using (Process process = Process.Start(annotateSnpEff))
            {
                using (StreamReader reader = process.StandardOutput)
                {
                    string result = reader.ReadToEnd();
                    annotatedVCF.Write(result + "\n");
                }
            }

            annotatedVCF.Close();

            //parse output
            ParseVCF annotatedVCFFile = new ParseVCF(analysisDir + @"\AnnotatedVariants.vcf", parameters);

            return annotatedVCFFile;
        }
    }
}
