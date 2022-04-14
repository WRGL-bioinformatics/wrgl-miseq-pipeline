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
            StringBuilder samtoolsSortBamParameters = new StringBuilder();
            StringBuilder samtoolsIndexBamParameters = new StringBuilder();
            StringBuilder realignerTargetCreatorParameters = new StringBuilder();
            StringBuilder indelRealignerParameters = new StringBuilder();
            StringBuilder geminiIndelRealignerParameters = new StringBuilder();

            // Configure parameters for the various tools and subprocesses.
            alignmentParameters.Append(sampleSheet.Analyses["G"]);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item1);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item2);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append($@"{analysisDir}\{record.Sample_ID}");

            samtoolsSortBamParameters.Append(@"sort ");
            samtoolsSortBamParameters.Append(@"-O BAM ");
            samtoolsSortBamParameters.Append($@"-o {analysisDir}\{record.Sample_ID}_sorted.bam ");
            samtoolsSortBamParameters.Append($@"{analysisDir}\{record.Sample_ID}.sam");

            samtoolsIndexBamParameters.Append($@"index {analysisDir}\{record.Sample_ID}_sorted.bam ");
            samtoolsIndexBamParameters.Append($@"{analysisDir}\{record.Sample_ID}_sorted.bai");

            realignerTargetCreatorParameters.Append(@"-Xmx2g ");
            realignerTargetCreatorParameters.Append($@"-jar {parameters.GatkPath} ");
            realignerTargetCreatorParameters.Append(@"-T RealignerTargetCreator ");
            realignerTargetCreatorParameters.Append($@"-R {parameters.GenotypingReferenceFastaPath} ");
            realignerTargetCreatorParameters.Append($@"-I {analysisDir}\{record.Sample_ID}_sorted.bam ");
            realignerTargetCreatorParameters.Append($@"-o {analysisDir}\{record.Sample_ID}_RTC.intervals ");
            realignerTargetCreatorParameters.Append(@"-dt NONE ");
            realignerTargetCreatorParameters.Append($@"-known {parameters.KnownIndels1Path} ");
            realignerTargetCreatorParameters.Append($@"-known {parameters.KnownIndels2Path} ");
            realignerTargetCreatorParameters.Append($@"-known {parameters.KnownIndels3Path} ");
            realignerTargetCreatorParameters.Append(@"-ip 100 ");
            realignerTargetCreatorParameters.Append($@"-L {analysisDir}\GenotypingRegions.bed ");
            realignerTargetCreatorParameters.Append(@"-et NO_ET ");
            realignerTargetCreatorParameters.Append($@"-K {parameters.GatkKeyPath}");

            indelRealignerParameters.Append($@"-Xmx2g -jar {parameters.GatkPath} ");
            indelRealignerParameters.Append(@"-T IndelRealigner ");
            indelRealignerParameters.Append($@"-R {parameters.GenotypingReferenceFastaPath} ");
            indelRealignerParameters.Append($@"-I {analysisDir}\{record.Sample_ID}_sorted.bam ");
            indelRealignerParameters.Append($@"-targetIntervals {analysisDir}\{record.Sample_ID}_RTC.intervals ");
            indelRealignerParameters.Append($@"-o {analysisDir}\{record.Sample_ID}.bam ");
            indelRealignerParameters.Append($@"-known {parameters.KnownIndels1Path} ");
            indelRealignerParameters.Append($@"-known {parameters.KnownIndels2Path} ");
            indelRealignerParameters.Append($@"-known {parameters.KnownIndels3Path} ");
            indelRealignerParameters.Append(@"-dt NONE ");
            indelRealignerParameters.Append(@"--maxConsensuses 300000 ");
            indelRealignerParameters.Append(@"--maxReadsForConsensuses 1200000 ");
            indelRealignerParameters.Append(@"--maxReadsForRealignment 200000000 ");
            indelRealignerParameters.Append(@"--LODThresholdForCleaning 0.4 ");
            indelRealignerParameters.Append(@"-et NO_ET ");
            indelRealignerParameters.Append($@"-K {parameters.GatkKeyPath}");

            geminiIndelRealignerParameters.Append(parameters.GeminiMultiPath);
            geminiIndelRealignerParameters.Append(@" ");
            geminiIndelRealignerParameters.Append($@"--numprocesses {parameters.GeminiMultiThreads} ");
            // Stops time-consuming analysis of additional contigs.
            // DEV: This will almost certainly need editing or removing for GRCh38
            // DEV: TEST BEFORE LEAVING.
            geminiIndelRealignerParameters.Append(@"--chromosomes 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,X,Y ");
            geminiIndelRealignerParameters.Append($@"--samtools {parameters.SamtoolsPath} ");
            geminiIndelRealignerParameters.Append($@"--exepath {parameters.GeminiPath} ");
            geminiIndelRealignerParameters.Append($@"--genome {parameters.GenotypingReferenceFolderPath} ");
            geminiIndelRealignerParameters.Append($@"--outfolder {analysisDir}\{record.Sample_ID} ");
            geminiIndelRealignerParameters.Append($@"--bam {analysisDir}\{record.Sample_ID}_sorted.bam");

            // Set log file location for Genotyping processes. Initialise analysis within using
            // so we don't have to handle closing the StreamWriter
            using (StreamWriter logFile = new StreamWriter($@"{analysisDir}\{record.Sample_ID}_genotyping.log"))
            {
                // Align reads
                ProcessStartInfo ampliconAlignerV2 = new ProcessStartInfo
                {
                    FileName = parameters.AmpliconAlignerPath,
                    Arguments = alignmentParameters.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(ampliconAlignerV2))
                {
                    logFile.Write(process.StandardOutput.ReadToEnd());
                    logFile.Write(process.StandardError.ReadToEnd());
                    process.WaitForExit();
                }

                // Sort SAM and convert to BAM
                ProcessStartInfo samtoolsSortBam = new ProcessStartInfo
                {
                    FileName = parameters.SamtoolsPath,
                    Arguments = samtoolsSortBamParameters.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(samtoolsSortBam))
                {
                    logFile.Write(process.StandardOutput.ReadToEnd());
                    logFile.Write(process.StandardError.ReadToEnd());
                    process.WaitForExit();
                }

                // Index BAM
                ProcessStartInfo samtoolsIndexBam = new ProcessStartInfo
                {
                    FileName = parameters.SamtoolsPath,
                    Arguments = samtoolsIndexBamParameters.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(samtoolsIndexBam))
                {
                    logFile.Write(process.StandardOutput.ReadToEnd());
                    logFile.Write(process.StandardError.ReadToEnd());
                    process.WaitForExit();
                }

                // Check to see what indel realigner to use
                if (parameters.GenotypingUseGemini == true)
                {
                    ProcessStartInfo geminiIndelRealigner = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = geminiIndelRealignerParameters.ToString(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (Process process = Process.Start(geminiIndelRealigner))
                    {
                        logFile.Write(process.StandardOutput.ReadToEnd());
                        logFile.Write(process.StandardError.ReadToEnd());
                        process.WaitForExit();
                    }

                    // Move files from gemini analysis directory up to the main run folder
                    File.Copy($@"{analysisDir}\{record.Sample_ID}\{record.Sample_ID}_sorted.PairRealigned.bam", $@"{analysisDir}\{record.Sample_ID}.bam");
                    File.Copy($@"{analysisDir}\{record.Sample_ID}\{record.Sample_ID}_sorted.PairRealigned.bam.bai", $@"{analysisDir}\{record.Sample_ID}.bam.bai");
                    // Delete the temporary Gemini analysis folder
                    FileManagement.ForceDeleteDirectory($@"{analysisDir}\{record.Sample_ID}", parameters);
                }
                // If not told to use Gemini, fall back on Gatk IndelRealigner
                // DEV: This is no longer in use, and can probably be safely deleted
                //      Consider this once in-process changes have been tested.
                else
                {
                    // Create regions file to run realigner over
                    ProcessStartInfo realignerTargetCreator = new ProcessStartInfo
                    {
                        FileName = parameters.JavaPath,
                        Arguments = realignerTargetCreatorParameters.ToString(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using (Process process = Process.Start(realignerTargetCreator))
                    {
                        logFile.Write(process.StandardOutput.ReadToEnd());
                        logFile.Write(process.StandardError.ReadToEnd());
                        process.WaitForExit();
                    }

                    // Realign over intervals
                    ProcessStartInfo indelRealigner = new ProcessStartInfo
                    {
                        FileName = parameters.JavaPath,
                        Arguments = indelRealignerParameters.ToString(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (Process process = Process.Start(indelRealigner))
                    {
                        logFile.Write(process.StandardOutput.ReadToEnd());
                        logFile.Write(process.StandardError.ReadToEnd());
                        process.WaitForExit();
                    }
                    // Rename the index file from .bai to .bam.bai (just for consistency with existing data)
                    File.Move($@"{analysisDir}\{record.Sample_ID}.bai", $@"{analysisDir}\{record.Sample_ID}.bam.bai");
                }
            }
            // Cleanup files
            File.Delete($@"{analysisDir}\{record.Sample_ID}.sam");
            File.Delete($@"{analysisDir}\{record.Sample_ID}_unsorted.bam");
            File.Delete($@"{analysisDir}\{record.Sample_ID}_sorted.bam");
            File.Delete($@"{analysisDir}\{record.Sample_ID}_sorted.bai");
            File.Delete($@"{analysisDir}\{record.Sample_ID}_RTC.intervals");
        }

        /// <summary>
        /// Run the variant caller - runs at a RUN level
        /// </summary>
        /// <param name="analysisDir">Run analysis directory</param>
        /// <param name="parameters">Configred ProgrammeParameters</param>
        public static void CallSomaticVariants(string analysisDir, ProgrammeParameters parameters)
        {
            StringBuilder somaticVariantCallerParameter = new StringBuilder();
            // Pisces is an updated development of SVC, with similar but slightly different params
            StringBuilder piscesVariantCallerParameters = new StringBuilder();

            somaticVariantCallerParameter.Append($@"-B {analysisDir} ");
            somaticVariantCallerParameter.Append($@"-g {parameters.GenotypingReferenceFolderPath} ");
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
            somaticVariantCallerParameter.Append($@"-r {analysisDir} ");
            somaticVariantCallerParameter.Append(@"-m 0");

            piscesVariantCallerParameters.Append($@"{parameters.PiscesPath} ");
            piscesVariantCallerParameters.Append($@"--bam {analysisDir} ");
            piscesVariantCallerParameters.Append($@"--genomefolders {parameters.GenotypingReferenceFolderPath} ");
            piscesVariantCallerParameters.Append(@"--minvf 0.01 ");
            piscesVariantCallerParameters.Append($@"--minbq {parameters.GenotypingQual} ");
            piscesVariantCallerParameters.Append($@"--minmq {parameters.GenotypingQual} ");
            piscesVariantCallerParameters.Append(@"--minvq 20 ");
            piscesVariantCallerParameters.Append(@"--maxvq 100 ");
            piscesVariantCallerParameters.Append($@"--mindpfilter {parameters.GenotypingDepth} ");
            piscesVariantCallerParameters.Append($@"--mindp {parameters.GenotypingDepth} ");
            piscesVariantCallerParameters.Append(@"--gvcf false ");
            piscesVariantCallerParameters.Append(@"--ssfilter false");

            // If the Pisces flag is set in the ini file, use it as the variant caller
            // otherwise use the SomaticVariantCaller
            // DEV: In the near future *only* Pisces will be available, so the check and options
            //      for SomaticVariantCaller will become redundant and can be removed.
            if (parameters.GenotypingUsePisces == true)
            {
                ProcessStartInfo callPiscesVariants = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = piscesVariantCallerParameters.ToString(),
                    UseShellExecute = false,
                };
                try
                {
                    using (Process process = Process.Start(callPiscesVariants))
                    {
                        process.WaitForExit();
                    }
                }
                catch
                {
                    AuxillaryFunctions.WriteLog("Could not start Pisces variant caller.", parameters, errorCode: -1);
                    throw;
                }
            }
            else
            {
                ProcessStartInfo callSomaticVariants = new ProcessStartInfo
                {
                    FileName = parameters.SomaticVariantCallerPath,
                    Arguments = somaticVariantCallerParameter.ToString(),
                    UseShellExecute = false,
                };
                
                try
                {
                    using (Process process = Process.Start(callSomaticVariants))
                    {
                        process.WaitForExit();
                    }
                }
                catch
                {
                    AuxillaryFunctions.WriteLog("Could not start Somatic Variant Caller.", parameters, errorCode: -1);
                    throw;
                }
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
            AuxillaryFunctions.WriteLog(@"Compressing variants for annotation...", parameters);

            // DEV: Can we set this up with 'using' so we don't have to manually close?
            
            HashSet<GenomicVariant> uniqueGenomicVariants = new HashSet<GenomicVariant>();

            using (StreamWriter compressedUnAnnotatedVariantsFile = new StreamWriter($@"{analysisDir}\UnannotatedVariants.vcf"))
            {
                // Write headers to UnannotatedVariants VCF file
                compressedUnAnnotatedVariantsFile.WriteLine("##fileformat=VCFv4.1");
                compressedUnAnnotatedVariantsFile.WriteLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

                // Loop over VCF files
                foreach (SampleRecord sampleRecord in sampleSheet.SampleRecords)
                {
                    if (sampleRecord.Analysis != "G")
                    {
                        continue;
                    }
                    string vcffile = Directory.GetFiles(analysisDir, $@"{sampleRecord.Sample_ID}*.vcf")[0];
                    ParseVCF parseVCFFile = new ParseVCF(vcffile, parameters);
                    // Loop over VCF entries
                    // SVC and Pisces assign different values to the sample ID in the VCF
                    // This is very annoying, but I can't think of a simple way around it...
                    string sampleid = "SampleID";
                    if (parameters.GenotypingUsePisces == true)
                    {
                        sampleid = $@"{sampleRecord.Sample_ID}.bam";
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
                }

                foreach (GenomicVariant variant in uniqueGenomicVariants)
                {
                    StringBuilder line = new StringBuilder();

                    // DEV: Test if this can be condensed into a single, clearer line
                    //line.Append(variant.CHROM);
                    //line.Append("\t");
                    //line.Append(variant.POS);
                    //line.Append("\t");
                    //line.Append(@".");
                    //line.Append("\t");
                    //line.Append(variant.REF);
                    //line.Append("\t");
                    //line.Append(variant.ALT);
                    //line.Append("\t");
                    //line.Append(@".");
                    //line.Append("\t");
                    //line.Append(@".");
                    //line.Append("\t");
                    //line.Append(@".");
                    //line.Append("\n");

                    line.Append($"{variant.CHROM}\t{variant.POS}\t.\t{variant.REF}\t{variant.ALT}\t.\t.\t.\n");
                    compressedUnAnnotatedVariantsFile.Write(line.ToString());
                }
            }
        }

        /// <summary>
        /// Runs SNPEff to annotated the unique VCF
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="analysisDir">Analysis folder of run</param>
        /// <returns>ParseVCF representation of the annotated VCF file</returns>
        public static ParseVCF CallSNPEff(ProgrammeParameters parameters, string analysisDir)
        {
            AuxillaryFunctions.WriteLog(@"Calling SnpEff annotator...", parameters);
            

            // snpEff parameters
            StringBuilder snpEffParameters = new StringBuilder();
            snpEffParameters.Append($@"-Xmx4g -jar {parameters.SnpEffPath} ");
            snpEffParameters.Append(@"-v GRCh37.75 ");
            snpEffParameters.Append(@"-noStats ");
            snpEffParameters.Append(@"-no-downstream ");
            snpEffParameters.Append(@"-no-intergenic ");
            snpEffParameters.Append(@"-no-upstream ");
            snpEffParameters.Append(@"-no INTRAGENIC ");
            snpEffParameters.Append(@"-spliceSiteSize 10 ");
            snpEffParameters.Append($@"-onlyTr {parameters.PreferredTranscriptsPath} ");
            snpEffParameters.Append(@"-noLog ");
            snpEffParameters.Append(@"-formatEff ");
            snpEffParameters.Append($@"{analysisDir}\UnannotatedVariants.vcf");

            // Generate annotated variants
            ProcessStartInfo annotateSnpEff = new ProcessStartInfo
            {
                FileName = parameters.JavaPath,
                Arguments = snpEffParameters.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false
            };

            // multiple usings can be chained together for simplicity
            using (StreamWriter annotatedVCF = new StreamWriter($@"{ analysisDir }\AnnotatedVariants.vcf"))
            using (Process process = Process.Start(annotateSnpEff))
            using (StreamReader reader = process.StandardOutput)
            {
                string result = reader.ReadToEnd();
                annotatedVCF.WriteLine(result);
            }
            // Parse output
            ParseVCF annotatedVCFFile = new ParseVCF($@"{analysisDir}\AnnotatedVariants.vcf", parameters);
            return annotatedVCFFile;
        }
    }
}
