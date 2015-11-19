﻿using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Web;
using System.Threading;

namespace Codaxy.WkHtmlToPdf
{
    public class PdfConvertException : Exception
    {
        public PdfConvertException(String msg) : base(msg) { }
    }

    public class PdfConvertTimeoutException : PdfConvertException
    {
        public PdfConvertTimeoutException() : base("HTML to PDF conversion process has not finished in the given period.") { }
    }

	public class PdfOutput
	{
		public String OutputFilePath { get; set; }
		public Stream OutputStream { get; set; }
		public Action<PdfDocument, byte[]> OutputCallback { get; set; }
	}

	public class PdfDocument
	{
		public String Url { get; set; }
		public String Html { get; set; }
		public String HeaderUrl { get; set; }
		public String FooterUrl { get; set; }
        public String HeaderLeft { get; set; }
        public String HeaderCenter { get; set; }
        public String HeaderRight { get; set; }
        public String FooterLeft { get; set; }
        public String FooterCenter { get; set; }
        public String FooterRight { get; set; }
		public object State { get; set; }
	}

	public class PdfConvertEnvironment
	{
		public String TempFolderPath { get; set; }
		public String WkHtmlToPdfPath { get; set; }
		public int Timeout { get; set; }
		public bool Debug { get; set; }
	}

    public class PdfConvert
    {
		static PdfConvertEnvironment _e;

		public static PdfConvertEnvironment Environment
		{
			get
			{
				if (_e == null)
					_e = new PdfConvertEnvironment
					{
						TempFolderPath = Path.GetTempPath(),
						WkHtmlToPdfPath = GetWkhtmlToPdfExeLocation(),
						Timeout = 60000
					};
				return _e;
			}
		}

        private static string GetWkhtmlToPdfExeLocation()
        {
            string exeLocationUnderBinFolder = @"wkhtmltopdf\bin\wkhtmltopdf.exe";
            string exeLocationUnderRootFolder = @"wkhtmltopdf\wkhtmltopdf.exe"; 

            string programFilesPath = System.Environment.GetEnvironmentVariable("ProgramFiles");
            string filePath = Path.Combine(programFilesPath, exeLocationUnderBinFolder );

            if (File.Exists(filePath))
                return filePath;

            string programFilesx86Path = System.Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string filex86Path = Path.Combine(programFilesx86Path, exeLocationUnderRootFolder);

            //case when both are returning the same path
            if (programFilesx86Path == programFilesPath)
            {
                int x86Pos = programFilesx86Path.IndexOf(" (x86)");
                if (x86Pos < 0) x86Pos = programFilesx86Path.IndexOf("(x86)");
                if (x86Pos > -1)
                {
                    programFilesx86Path = programFilesx86Path.Substring(0, programFilesx86Path.Length - (programFilesx86Path.Length - x86Pos));
                    filex86Path = Path.Combine(programFilesx86Path, exeLocationUnderBinFolder); 
                }
            }

            if (File.Exists(filex86Path))
                return filex86Path;


            filePath = Path.Combine(programFilesPath, exeLocationUnderBinFolder);
            if (File.Exists(filePath))
                return filePath;

            return Path.Combine(programFilesx86Path, exeLocationUnderBinFolder);
        }

		public static void ConvertHtmlToPdf(PdfDocument document, PdfOutput output)
		{
			ConvertHtmlToPdf(document, null, output);
		}

		public static void ConvertHtmlToPdf(PdfDocument document, PdfConvertEnvironment environment, PdfOutput woutput)
        {
            if (document.Url == "-" && document.Html == null)
                throw new PdfConvertException(
                    String.Format("You must supply a HTML string, if you have enterd the url: {0}", document.Url)
                );

			if (environment == null)
				environment = Environment;

			String outputPdfFilePath;
			bool delete;
			if (woutput.OutputFilePath != null)
			{
				outputPdfFilePath = woutput.OutputFilePath;
				delete = false;
			}
			else
			{
				outputPdfFilePath = Path.Combine(environment.TempFolderPath, String.Format("{0}.pdf", Guid.NewGuid()));
				delete = true;
			}

			if (!File.Exists(environment.WkHtmlToPdfPath))
				throw new PdfConvertException(String.Format("File '{0}' not found. Check if wkhtmltopdf application is installed.", environment.WkHtmlToPdfPath));
            
            StringBuilder paramsBuilder = new StringBuilder();
            paramsBuilder.Append("--page-size A4 ");
            
			if (!string.IsNullOrEmpty(document.HeaderUrl))
            {
				paramsBuilder.AppendFormat("--header-html {0} ", document.HeaderUrl);
                paramsBuilder.Append("--margin-top 25 ");
                paramsBuilder.Append("--header-spacing 5 ");
            }
			if (!string.IsNullOrEmpty(document.FooterUrl))
            {
				paramsBuilder.AppendFormat("--footer-html {0} ", document.FooterUrl);
                paramsBuilder.Append("--margin-bottom 25 ");
                paramsBuilder.Append("--footer-spacing 5 ");
            }

            if (!string.IsNullOrEmpty(document.HeaderLeft))
                paramsBuilder.AppendFormat("--header-left \"{0}\" ", document.HeaderLeft);

            if (!string.IsNullOrEmpty(document.FooterCenter))
                paramsBuilder.AppendFormat("--header-center \"{0}\" ", document.HeaderCenter);

            if (!string.IsNullOrEmpty(document.FooterCenter))
                paramsBuilder.AppendFormat("--header-right \"{0}\" ", document.HeaderRight);

            if (!string.IsNullOrEmpty(document.FooterLeft))
                paramsBuilder.AppendFormat("--footer-left \"{0}\" ", document.FooterLeft);

            if (!string.IsNullOrEmpty(document.FooterCenter))
                paramsBuilder.AppendFormat("--footer-center \"{0}\" ", document.FooterCenter);

            if (!string.IsNullOrEmpty(document.FooterCenter))
                paramsBuilder.AppendFormat("--footer-right \"{0}\" ", document.FooterRight);
            
			paramsBuilder.AppendFormat("\"{0}\" \"{1}\"", document.Url, outputPdfFilePath);
            
            try
            {
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();

                using (Process process = new Process())
                {
                    process.StartInfo.FileName = environment.WkHtmlToPdfPath;
                    process.StartInfo.Arguments = paramsBuilder.ToString();
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;

                    using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
                    using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
                    {
                        DataReceivedEventHandler outputHandler = (sender, e) =>
                        {
                            if (e.Data == null)
                            {
                                outputWaitHandle.Set();
                            }
                            else
                            {
                                output.AppendLine(e.Data);
                            }
                        };

                        DataReceivedEventHandler errorHandler = (sender, e) =>
                        {
                            if (e.Data == null)
                            {
                                errorWaitHandle.Set();
                            }
                            else
                            {
                                error.AppendLine(e.Data);
                            }
                        };

                        process.OutputDataReceived += outputHandler;
                        process.ErrorDataReceived += errorHandler;

                        try
                        {
                            process.Start();

                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            if (document.Html != null)
                            {
                                using (var stream = process.StandardInput)
                                {
                                    byte[] buffer = Encoding.UTF8.GetBytes(document.Html);
                                    stream.BaseStream.Write(buffer, 0, buffer.Length);
                                    stream.WriteLine();
                                }
                            }

                            if (process.WaitForExit(environment.Timeout) && outputWaitHandle.WaitOne(environment.Timeout) && errorWaitHandle.WaitOne(environment.Timeout))
                            {
                                if (process.ExitCode != 0 && !File.Exists(outputPdfFilePath))
                                {
                                    throw new PdfConvertException(String.Format("Html to PDF conversion of '{0}' failed. Wkhtmltopdf output: \r\n{1}", document.Url, error));
                                }
                            }
                            else
                            {
                                if (!process.HasExited)
                                    process.Kill();

                                throw new PdfConvertTimeoutException();
                            }
                        }
                        finally
                        {
                            process.OutputDataReceived -= outputHandler;
                            process.ErrorDataReceived -= errorHandler;
                        }
                    }
                }


                if (woutput.OutputStream != null)
                {
                    using (Stream fs = new FileStream(outputPdfFilePath, FileMode.Open))
                    {
                        byte[] buffer = new byte[32 * 1024];
                        int read;

                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                            woutput.OutputStream.Write(buffer, 0, read);
                    }
                }

                if (woutput.OutputCallback != null)
                {
                    byte[] pdfFileBytes = File.ReadAllBytes(outputPdfFilePath);
                    woutput.OutputCallback(document, pdfFileBytes);
                }

            }            
            finally
            {
                if (delete && File.Exists(outputPdfFilePath))
                    File.Delete(outputPdfFilePath);
            }
        }        
    }

    //class OSUtil
    //{
    //    public static string GetProgramFilesx86Path()
    //    {
    //        if (8 == IntPtr.Size || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
    //        {
    //            return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
    //        }
    //        return Environment.GetEnvironmentVariable("ProgramFiles");
    //    }
    //}

	//public static class HttpResponseExtensions
	//{
	//    public static void SendFileForDownload(this HttpResponse response, String filename, byte[] content)
	//    {
	//        SetFileDownloadHeaders(response, filename);
	//        response.OutputStream.Write(content, 0, content.Length);
	//        response.Flush();
	//    }

	//    public static void SendFileForDownload(this HttpResponse response, String filename)
	//    {
	//        SetFileDownloadHeaders(response, filename);
	//        response.TransmitFile(filename);
	//        response.Flush();
	//    }

	//    public static void SetFileDownloadHeaders(this HttpResponse response, String filename)
	//    {
	//        FileInfo fi = new FileInfo(filename);
	//        response.ContentType = "application/force-download";
	//        response.AddHeader("Content-Disposition", "attachment; filename=\"" + fi.Name + "\"");
	//    }
	//}
}
