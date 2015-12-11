using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using NLog;
using NLog.Targets;

namespace GrowingData.Pipeliner {
	public class Program {

		public static void Main(string[] args) {
			var logger = LogManager.GetLogger("PipelineHost");


			string path = Directory.GetCurrentDirectory();
			if (args.Length > 0) {
				path = args[0];
			}

			Console.WriteLine("Running pipeline from: {0}", path);
			var host = new PipelineHost(path);

			try {
				if (host.Compile()) {
					Console.WriteLine("Compilation success!");
				} else {
					Console.WriteLine("Compilation failed!");
					return;

				}
			} catch (Exception ex) {
				logger.Debug(ex, "Unable to load Script");
				return;
			}

			var pipe = host.Pipeline;
			Console.WriteLine("Loaded Pipeline: {0}, please enter a step to run:", pipe.Name);
			for (var i = 0; i < pipe.Steps.Count; i++) {
				var step = pipe.Steps[i];
				Console.WriteLine("[{0}]	{1}", i, step.StepName);
			}

			var input = Console.ReadLine();
			var runAfter = input.Contains("*");
			input = input.Replace("*", "");

			int stepNumber = 0;

			if (!int.TryParse(input, out stepNumber)) {
				Console.WriteLine("Please enter a step number, not gibberish.");
				return;

			}
			for (var i = 0; i < pipe.Steps.Count; i++) {
				var step = pipe.Steps[i];
				if (i == stepNumber || (runAfter && i >= stepNumber)) {
					if (!pipe.RunStep(step.StepName)) {
						Console.WriteLine("Step {0} (1) failed.", step.StepName, i);
						break;
					}
				}
			}
		}
	}
}
