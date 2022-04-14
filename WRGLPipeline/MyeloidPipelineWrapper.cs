using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WRGLPipeline
{
    class MyeloidPipelineWrapper
    {
        // DEV: sampleSheet and fastqFileNames aren't really needed for this, but 
        //      I've included the so it matches the other pipeline wrappers.
        readonly private ParseSampleSheet sampleSheet;
        readonly private ProgrammeParameters parameters;
        /// <summary>
        /// Runs the myeloid coverage and trasfer Python app.
        /// </summary>
        /// <param name="parameters">Configured ProgrammeParameters</param>
        /// <param name="sampleSheet">Parsed SampleSheet</param>
        public MyeloidPipelineWrapper(ParseSampleSheet sampleSheet, ProgrammeParameters parameters)
        {
            this.sampleSheet = sampleSheet;
            this.parameters = parameters;

            ExecuteMyeloidPipeline();
        }
        /// <summary>
        /// Configures and runs the process for the myeloid app.
        /// </summary>
        public void ExecuteMyeloidPipeline()
        {
            AuxillaryFunctions.WriteLog($@"Starting myeloid post-run processing for {this.sampleSheet.ExperimentName}...", parameters);

            // The myeloid coverage script needs to get the Analysis folder for the run
            // this is passed to the pipeline by MiSeq Reporter as the only arg, but to match
            // the other pipeline wrappers it isn't passed directly to here. 
            // It should be in the parameters object, as SuppliedDir
            AuxillaryFunctions.WriteLog($@"Running myeloid coverage on {parameters.SuppliedDir}", parameters);

            // Create a logfile to record the tool output
            using (StreamWriter logFile = new StreamWriter($@"{parameters.LocalRootRunDir}\MyeloidTransfer.log"))
            {
                ProcessStartInfo myeloidTransfer = new ProcessStartInfo
                {
                    FileName = parameters.MyeloidCoverageTool,
                    Arguments = parameters.SuppliedDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(myeloidTransfer))
                {
                    logFile.Write(process.StandardOutput.ReadToEnd());
                    logFile.Write(process.StandardError.ReadToEnd());
                    process.WaitForExit();
                }
            }
            AuxillaryFunctions.WriteLog(@"Myeloid transfer complete.", parameters);
        }
    }
}
