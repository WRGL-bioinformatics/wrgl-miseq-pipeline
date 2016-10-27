using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace WRGLPipeline
{
    class GenerateGenotypingVCFs
    {
        SampleRecord record;
        string analysisDir, logFilename;
        ProgrammeParameters parameters;
        ParseSampleSheet sampleSheet;
        Tuple<string, string> fastqFileNames;

        public GenerateGenotypingVCFs(SampleRecord _record, string _analysisDir, string _logFilename, ProgrammeParameters _parameters, ParseSampleSheet _sampleSheet, Tuple<string, string> _fastqFilenames)
        {
            this.record = _record;
            this.analysisDir = _analysisDir;
            this.logFilename = _logFilename;
            this.parameters = _parameters;
            this.sampleSheet = _sampleSheet;
            this.fastqFileNames = _fastqFilenames;
        }

        public void MapReads()
        {
            StringBuilder alignmentParameters = new StringBuilder();
            StringBuilder samtoolsSamtoBamParameters = new StringBuilder();
            StringBuilder samtoolsSortBamParameters = new StringBuilder();
            StringBuilder samtoolsIndexBamParameters = new StringBuilder();
            StringBuilder realignerTargetCreatorParameters = new StringBuilder();
            StringBuilder indelRealignerParameters = new StringBuilder();

            StreamWriter logFile = new StreamWriter(analysisDir + @"\" + record.Sample_ID + @"_genotyping.log");

            alignmentParameters.Append(sampleSheet.getAnalyses["G"]);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item1);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(fastqFileNames.Item2);
            alignmentParameters.Append(@" ");
            alignmentParameters.Append(analysisDir);
            alignmentParameters.Append(@"\");
            alignmentParameters.Append(record.Sample_ID);

            samtoolsSamtoBamParameters.Append(@"view ");
            samtoolsSamtoBamParameters.Append(@"-b ");
            samtoolsSamtoBamParameters.Append(@"-S ");
            samtoolsSamtoBamParameters.Append(@"-o ");
            samtoolsSamtoBamParameters.Append(analysisDir);
            samtoolsSamtoBamParameters.Append(@"\");
            samtoolsSamtoBamParameters.Append(record.Sample_ID);
            samtoolsSamtoBamParameters.Append(@"_unsorted.bam ");
            samtoolsSamtoBamParameters.Append(analysisDir);
            samtoolsSamtoBamParameters.Append(@"\");
            samtoolsSamtoBamParameters.Append(record.Sample_ID);
            samtoolsSamtoBamParameters.Append(@".sam");

            samtoolsSortBamParameters.Append(@"sort ");
            samtoolsSortBamParameters.Append(analysisDir);
            samtoolsSortBamParameters.Append(@"\");
            samtoolsSortBamParameters.Append(record.Sample_ID);
            samtoolsSortBamParameters.Append(@"_unsorted.bam ");
            samtoolsSortBamParameters.Append(analysisDir);
            samtoolsSortBamParameters.Append(@"\");
            samtoolsSortBamParameters.Append(record.Sample_ID);
            samtoolsSortBamParameters.Append(@"_sorted");

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
            realignerTargetCreatorParameters.Append(parameters.getGatkPath);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-T RealignerTargetCreator ");
            realignerTargetCreatorParameters.Append(@"-R ");
            realignerTargetCreatorParameters.Append(parameters.getb37FastaPath);
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
            realignerTargetCreatorParameters.Append(parameters.getKnownIndels1Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-known ");
            realignerTargetCreatorParameters.Append(parameters.getKnownIndels2Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-known ");
            realignerTargetCreatorParameters.Append(parameters.getKnownIndels3Path);
            realignerTargetCreatorParameters.Append(@" ");
            realignerTargetCreatorParameters.Append(@"-ip 100 ");
            realignerTargetCreatorParameters.Append(@"-L ");
            realignerTargetCreatorParameters.Append(analysisDir);
            realignerTargetCreatorParameters.Append(@"\GenotypingRegions.bed ");
            realignerTargetCreatorParameters.Append(@"-et NO_ET ");
            realignerTargetCreatorParameters.Append(@"-K ");
            realignerTargetCreatorParameters.Append(parameters.getGatkKeyPath);

            indelRealignerParameters.Append(@"-Xmx4g -jar ");
            indelRealignerParameters.Append(parameters.getGatkPath);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-T IndelRealigner ");
            indelRealignerParameters.Append(@"-R ");
            indelRealignerParameters.Append(parameters.getb37FastaPath);
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
            indelRealignerParameters.Append(parameters.getKnownIndels1Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-known ");
            indelRealignerParameters.Append(parameters.getKnownIndels2Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-known ");
            indelRealignerParameters.Append(parameters.getKnownIndels3Path);
            indelRealignerParameters.Append(@" ");
            indelRealignerParameters.Append(@"-dt NONE ");
            indelRealignerParameters.Append(@"--maxConsensuses 300000 ");
            indelRealignerParameters.Append(@"--maxReadsForConsensuses 1200000 ");
            indelRealignerParameters.Append(@"--maxReadsForRealignment 200000000 ");
            indelRealignerParameters.Append(@"--LODThresholdForCleaning 0.4 ");
            indelRealignerParameters.Append(@"-et NO_ET ");
            indelRealignerParameters.Append(@"-K ");
            indelRealignerParameters.Append(parameters.getGatkKeyPath);

            //align reads
            Process ampliconAlignerV2 = new Process();
            ampliconAlignerV2.StartInfo.FileName = parameters.getAmpliconAlignerPath;
            ampliconAlignerV2.StartInfo.Arguments = alignmentParameters.ToString();
            ampliconAlignerV2.StartInfo.UseShellExecute = false;
            ampliconAlignerV2.StartInfo.RedirectStandardOutput = true;
            ampliconAlignerV2.StartInfo.RedirectStandardError = true;
            ampliconAlignerV2.Start();
            logFile.Write(ampliconAlignerV2.StandardOutput.ReadToEnd());
            logFile.Write(ampliconAlignerV2.StandardError.ReadToEnd());

            ampliconAlignerV2.WaitForExit();
            ampliconAlignerV2.Close();

            //convert sam to bam
            Process samtoolsSamtoBam = new Process();
            samtoolsSamtoBam.StartInfo.FileName = parameters.getSamtoolsPath;
            samtoolsSamtoBam.StartInfo.Arguments = samtoolsSamtoBamParameters.ToString();
            samtoolsSamtoBam.StartInfo.UseShellExecute = false;
            samtoolsSamtoBam.StartInfo.RedirectStandardOutput = true;
            samtoolsSamtoBam.StartInfo.RedirectStandardError = true;
            samtoolsSamtoBam.Start();
            logFile.Write(samtoolsSamtoBam.StandardOutput.ReadToEnd());
            logFile.Write(samtoolsSamtoBam.StandardError.ReadToEnd());

            //sort Bam
            Process samtoolsSortBam = new Process();
            samtoolsSortBam.StartInfo.FileName = parameters.getSamtoolsPath;
            samtoolsSortBam.StartInfo.Arguments = samtoolsSortBamParameters.ToString();
            samtoolsSortBam.StartInfo.UseShellExecute = false;
            samtoolsSortBam.StartInfo.RedirectStandardOutput = true;
            samtoolsSortBam.StartInfo.RedirectStandardError = true;
            samtoolsSortBam.Start();
            logFile.Write(samtoolsSortBam.StandardOutput.ReadToEnd());
            logFile.Write(samtoolsSortBam.StandardError.ReadToEnd());

            //index bam
            Process samtoolsIndexBam = new Process();
            samtoolsIndexBam.StartInfo.FileName = parameters.getSamtoolsPath;
            samtoolsIndexBam.StartInfo.Arguments = samtoolsIndexBamParameters.ToString();
            samtoolsIndexBam.StartInfo.UseShellExecute = false;
            samtoolsIndexBam.StartInfo.RedirectStandardOutput = true;
            samtoolsIndexBam.StartInfo.RedirectStandardError = true;
            samtoolsIndexBam.Start();
            logFile.Write(samtoolsIndexBam.StandardOutput.ReadToEnd());
            logFile.Write(samtoolsIndexBam.StandardError.ReadToEnd());

            //create regions file to run realigner over
            Process realignerTargetCreator = new Process();
            realignerTargetCreator.StartInfo.FileName = parameters.getJavaPath;
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
            indelRealigner.StartInfo.FileName = parameters.getJavaPath;
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

            //cleanup files
            File.Delete(analysisDir + @"\" + record.Sample_ID + @".sam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_unsorted.bam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_sorted.bam");
            File.Delete(analysisDir + @"\" + record.Sample_ID + @"_sorted.bai");
            File.Move(analysisDir + @"\" + record.Sample_ID + @".bai", analysisDir + @"\" + record.Sample_ID + @".bam.bai");
        }

        public static void CallSomaticVariants(string analysisDir, ProgrammeParameters parameters)
        {
            StringBuilder somaticVariantCallerParameter = new StringBuilder();

            somaticVariantCallerParameter.Append(@"-B ");
            somaticVariantCallerParameter.Append(analysisDir);
            somaticVariantCallerParameter.Append(@" ");
            somaticVariantCallerParameter.Append(@"-g ");
            somaticVariantCallerParameter.Append(parameters.getb37FilePath);
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

            //realign over intervals
            Process callSomaticVariants = new Process();
            callSomaticVariants.StartInfo.FileName = parameters.getSomaticVariantCallerPath;
            callSomaticVariants.StartInfo.Arguments = somaticVariantCallerParameter.ToString();
            callSomaticVariants.StartInfo.UseShellExecute = false;
            callSomaticVariants.Start();

            callSomaticVariants.WaitForExit();
            callSomaticVariants.Close();
        }

        public static void CompressVariants(string logFilename, ProgrammeParameters parameters, ParseSampleSheet sampleSheet, string analysisDir) //create unique list of variant passing QC for annotation
        {
            AuxillaryFunctions.WriteLog(@"Compressing variants for annotation...", logFilename, 0, false, parameters);

            StreamWriter compressedUnAnnotatedVariantsFile = new StreamWriter(analysisDir + @"\UnannotatedVariants.vcf");
            GenomicVariant tempVariant;
            HashSet<GenomicVariant> uniqueGenomicVariants = new HashSet<GenomicVariant>();

            //write headers to UnannotatedVariants VCF file
            compressedUnAnnotatedVariantsFile.Write("##fileformat=VCFv4.1\n");
            compressedUnAnnotatedVariantsFile.Write("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\n");

            //loop over VCF files
            foreach (SampleRecord sampleRecord in sampleSheet.getSampleRecords)
            {
                if (sampleRecord.Analysis != "G")
                {
                    continue;
                }

                //parse VCF and bank entries
                ParseVCF parseVCFFile = new ParseVCF(analysisDir + @"\" + sampleRecord.Sample_ID + "_S999.vcf", logFilename, parameters);

                //loop over VCF entries
                foreach (VCFRecordWithGenotype record in parseVCFFile.getVCFRecords["SampleID"])
                {
                    //store variants that pass qc
                    if (record.FILTER == "PASS" && record.QUAL >= 30 && System.Convert.ToUInt32(record.infoSubFields["DP"]) >= 1000)
                    {
                        tempVariant.CHROM = record.CHROM;
                        tempVariant.REF = record.REF;
                        tempVariant.ALT = record.ALT;
                        tempVariant.POS = record.POS;

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

        public static ParseVCF CallSNPEff(string logFilename, ProgrammeParameters parameters, string analysisDir)
        {
            AuxillaryFunctions.WriteLog(@"Calling SnpEff annotator...", logFilename, 0, false, parameters);
            StreamWriter annotatedVCF = new StreamWriter(analysisDir + @"\AnnotatedVariants.vcf");
            StringBuilder snpEffParameters = new StringBuilder();

            //build snpEff parameters
            snpEffParameters.Append(@"-Xmx4g -jar ");
            snpEffParameters.Append(parameters.getSnpEffPath);
            snpEffParameters.Append(" ");
            snpEffParameters.Append(@"-v GRCh37.75 ");
            snpEffParameters.Append(@"-noStats ");
            snpEffParameters.Append(@"-no-downstream ");
            snpEffParameters.Append(@"-no-intergenic ");
            snpEffParameters.Append(@"-no-upstream ");
            snpEffParameters.Append(@"-spliceSiteSize 10 ");
            snpEffParameters.Append(@"-onlyTr ");
            snpEffParameters.Append(parameters.getPreferredTranscriptsFile);
            snpEffParameters.Append(@" ");
            snpEffParameters.Append(@"-noLog "); //sends data back to snpEff; disabled
            snpEffParameters.Append(analysisDir);
            snpEffParameters.Append(@"\UnannotatedVariants.vcf");

            //annotated variants
            ProcessStartInfo annotateSnpEff = new ProcessStartInfo();
            annotateSnpEff.FileName = parameters.getJavaPath;
            annotateSnpEff.Arguments = snpEffParameters.ToString();
            annotateSnpEff.UseShellExecute = false;
            annotateSnpEff.RedirectStandardOutput = true;
            annotateSnpEff.RedirectStandardError = false;

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
            ParseVCF annotatedVCFFile = new ParseVCF(analysisDir + @"\AnnotatedVariants.vcf", logFilename, parameters);

            return annotatedVCFFile;
        }
    }
}
