using System;
using CommandLine;

namespace WRGLPipeline
{
    class ParseArgs
    {
        // Make the parsed args available as public properties
        // e.g. parser.getData
        public bool GetData { get; private set; }
        public bool CopyToNetwork { get; private set; }
        public string Path { get; private set; }
        /// <summary>
        /// Define the allowed arguments.
        /// --getData and --skipCopyToNetwork are simple boolean flags where presence == true
        /// path assumes that anything else is the path to the alignment folder.
        /// Will automatically generate a help message if incorrect arguments are identified.
        /// </summary>
        /// <remarks>
        /// DEV: It would be useful to ensure that the whole program is halted on any exceptions,
        ///      but I haven't figured that out yet. 
        /// </remarks>
        public class Options
        {
            // Check for the getdata flag
            [Option('g', "getData", Required = false,
                    Default = false, HelpText = "Download run data only")]
            public bool GetData { get; set; }

            // Check for the copy to network flag
            [Option('z', "skipCopyToNetwork", Required = false,
                    Default = false, HelpText = "Enable copy run data to network")]
            public bool SkipCopyToNetwork { get; set; }

            // Capture the first of any positional args - this should be the path
            // Anything else will be discarded.
            [Value(0)]
            public string Path { get; set; }
        }
        
        /// <summary>
        /// Use the CommandlineParse NuGet package to read command line args
        /// </summary>
        /// <param name="args">Cmd line arguments passed from the main function</param>
        /// <remarks>
        /// NOTE: Parser is configured to be case insensitive.
        /// </remarks>
        public ParseArgs(string[] args) //, ref ProgrammeParameters parameters)
        {
            // Create the parser here, so we can configure it as case insensitive
            // NOTE: This can probably be done directly as Parser(settings => etc.).ParseArguments
            //       but I'm not sure that's very clear... Or maybe not, but I just can't figure it out.
            var parser = new Parser(settings =>
            {
                settings.CaseSensitive = false;
                settings.HelpWriter = Console.Error;
            });

            // Parse the arguments, and assign to the appropriate variables
            parser.ParseArguments<Options>(args).WithParsed<Options>(options =>
            {
                GetData = options.GetData;
                // We want to reverese the skipCopyToNetwork option, as the code is written
                // assuming the flag would specify you should copy
                CopyToNetwork = !options.SkipCopyToNetwork;
                // This should be asserted as an actual file/directory elsewhere
                Path = options.Path;
            });
        }
    }
}
