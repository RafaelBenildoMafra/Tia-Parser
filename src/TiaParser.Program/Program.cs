using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Collections.Generic;
using System.Windows.Forms;
using NLog;
using System.Linq;
using TiaParser.Driver;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Program
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Logger logger = NLog
            .LogManager.Setup()
            .LoadConfigurationFromFile("nlog.config")
            .GetCurrentClassLogger();

            Console.Title = "TIA Parser";            
            string file = "";

            logger.Warn("Please specify the TIA system file as a parameter!");

            LoadTiaPathFromDialog(logger, ref file);

            TiaParserDriver tiaParser = new TiaParserDriver(file);            

            List<TiaAddress> tiaBlockAddresses = tiaParser.ParseTiaReferenceAddresses();

            string exportPath = Path.GetDirectoryName(file);

            WriteAddressesToFile(exportPath, tiaBlockAddresses);
        }

        private static void LoadTiaPathFromDialog(Logger logger, ref string file)
        {
            OpenFileDialog op = new OpenFileDialog
            {
                Filter = "TIA System File (.plf)|*.plf",
                CheckFileExists = false,
                ValidateNames = false
            };
            var ret = op.ShowDialog();

            if (ret == DialogResult.OK)
                file = op.FileName;
            else
            {
                logger.Warn("Please specify the TIA system file as a parameter!");
            }
        }

        static void WriteAddressesToFile(string filePath, List<TiaAddress> tiaAddresses)
        {
            string exportFilePath = Path.Combine(filePath, "export.txt");

            using (StreamWriter writer = new StreamWriter(exportFilePath))
            {
                foreach (TiaAddress address in tiaAddresses)
                {
                    writer.WriteLine($"{address.Name}, {address.ReferenceAddress}");
                }
            }

            Console.WriteLine($"Export file created at: {exportFilePath}");
        }
    }
}
