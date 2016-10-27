using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace WRGLPipeline
{
    class Coverage //need to return minDP for genotyping pipeline
    {
        //executes samtools depth function
        public static Dictionary<string, HashSet<string>> GetFailedAmpliconsFromBAMs(string logFilename, UInt32 minBaseQuality, UInt32 minMappingQuality, string bedPath, List<SampleRecord> sampleRecords,
            string outputDir, List<BEDRecord> targetBEDRecords, List<BEDRecord> coreBEDRecords, UInt32 minCoverage, ProgrammeParameters parameters)
        {
            AuxillaryFunctions.WriteLog(@"Calculating coverage values...", logFilename, 0, false, parameters);
            List<string> sampleIDs = new List<string>();

            StringBuilder samtoolsDepthParameter = new StringBuilder();

            samtoolsDepthParameter.Append(@"depth ");
            samtoolsDepthParameter.Append(@"-q ");
            samtoolsDepthParameter.Append(minBaseQuality);
            samtoolsDepthParameter.Append(' ');
            samtoolsDepthParameter.Append(@"-Q ");
            samtoolsDepthParameter.Append(minMappingQuality);
            samtoolsDepthParameter.Append(' ');
            samtoolsDepthParameter.Append(@"-b ");
            samtoolsDepthParameter.Append(bedPath);
            samtoolsDepthParameter.Append(' ');

            foreach (SampleRecord record in sampleRecords)
            {
                samtoolsDepthParameter.Append(outputDir);
                samtoolsDepthParameter.Append(@"\");
                samtoolsDepthParameter.Append(record.Sample_ID);
                samtoolsDepthParameter.Append(@".bam ");

                sampleIDs.Add(record.Sample_ID);
            }

            //annotated variants
            Process samtoolsDepth = new Process();
            samtoolsDepth.StartInfo.FileName = parameters.GetSamtoolsPath;
            samtoolsDepth.StartInfo.Arguments = samtoolsDepthParameter.ToString();
            samtoolsDepth.StartInfo.UseShellExecute = false;
            samtoolsDepth.StartInfo.RedirectStandardOutput = true;
            samtoolsDepth.StartInfo.RedirectStandardError = false;
            samtoolsDepth.Start();

            string samtoolsDepthOutput = samtoolsDepth.StandardOutput.ReadToEnd();

            samtoolsDepth.WaitForExit();
            samtoolsDepth.Close();

            return AnalyseCoverageData(samtoolsDepthOutput, logFilename, targetBEDRecords, coreBEDRecords, sampleIDs, minCoverage, parameters);
        }

        private static Dictionary<string, HashSet<string>> AnalyseCoverageData(string samtoolsDepthOutput, string logFilename, List<BEDRecord> targetBEDRecords, List<BEDRecord> coreBEDRecords, List<string> sampleIDs, UInt32 minCoverage, ProgrammeParameters parameters)
        {
            AuxillaryFunctions.WriteLog(@"Analysing coverage data...", logFilename, 0, false, parameters);

            string line, failedAmpliconID;
            UInt32 pos;
            Dictionary<Tuple<string, UInt32>, bool> isBaseCovered = new Dictionary<Tuple<string, UInt32>, bool>(); //bool = observed
            Dictionary<string, HashSet<string>> failedAmplicons = new Dictionary<string, HashSet<string>>();

            //add sampleIDs to dictionary of hashset
            foreach (string sampleID in sampleIDs)
            {
                failedAmplicons.Add(sampleID, new HashSet<string>());
            }

            //loop over target ROI, hash bases
            foreach (BEDRecord BEDRecord in targetBEDRecords)
            {
                //iterate over region
                //for (pos = BEDRecord.start + 2; pos < BEDRecord.end + 1; ++pos)
                for (pos = BEDRecord.start + 2; pos < BEDRecord.end + 1; ++pos)
                {
                    if (!isBaseCovered.ContainsKey(new Tuple<string, uint>(BEDRecord.chromosome, pos)))
                    {
                        isBaseCovered.Add(new Tuple<string, uint>(BEDRecord.chromosome, pos), false);
                    }
                }

            }

            // loop over output and assign failed to low coverage amplicons
            using (StringReader reader = new StringReader(samtoolsDepthOutput))
            {
                // Loop over the lines in the string.
                while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split('\t');

                    pos = Convert.ToUInt32(fields[1]);

                    //mark base as observed in the dataset
                    isBaseCovered[new Tuple<string, UInt32>(fields[0], pos)] = true;

                    for (int n = 2; n < fields.Length; ++n) //skip chrom & pos
                    {
                        if (Convert.ToUInt32(fields[n]) < minCoverage) //base has failed
                        {
                            //mark amplicon as failed
                            failedAmpliconID = AuxillaryFunctions.LookupAmpliconID(new Tuple<string, UInt32>(fields[0], pos), coreBEDRecords);

                            if (failedAmpliconID != "") //skip off target
                            {
                                failedAmplicons[sampleIDs[n - 2]].Add(failedAmpliconID);
                            }
                        }
                    }
                }
            }

            //report missing bases as failed
            foreach (KeyValuePair<Tuple<string, UInt32>, bool> nucl in isBaseCovered)
            {
                if (nucl.Value == false) //base not present in dataset
                {
                    failedAmpliconID = AuxillaryFunctions.LookupAmpliconID(nucl.Key, coreBEDRecords);

                    if (failedAmpliconID != "") //skip off target
                    {
                        foreach (string sampleID in sampleIDs)
                        {
                            //mark amplicon as failed
                            failedAmplicons[sampleID].Add(failedAmpliconID);
                        }
                    }

                }
            }

            return failedAmplicons;
        }
    }
}
