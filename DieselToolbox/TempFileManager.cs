﻿using DieselEngineFormats.Bundle;
using DieselEngineFormats.Utils;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DieselToolbox
{
    public class FileViewerManager : IDisposable
    {
        public class TempFile : IDisposable
        {
            private FileStream fs;

            public TempFile(FileEntry entry, BundleFileEntry be = null, dynamic exporter = null)
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Definitions.TempDir, entry.Name);

                if (exporter?.extension != null)
                    Path += "." + exporter.extension;

                dynamic file_data = entry.FileData(be, exporter);

                if (file_data is byte[])
                    File.WriteAllBytes(Path, file_data);
                else if (file_data is Stream)
                {
                    FileStream file_stream = File.Create(Path);
                    ((Stream)file_data).CopyTo(file_stream);
                    file_stream.Close();
                }
                else if (file_data is string)
                    File.WriteAllText(Path, file_data);
                else if (file_data is string[])
                    File.WriteAllLines(Path, file_data);

                //fs = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1, FileOptions.DeleteOnClose );

                Entry = be;
                ExporterKey = exporter?.key;
            }

            ~TempFile()
            {
                this.Dispose();
            }

            public string Path { get; set; }

            public Process RunProcess { get; set; }

            public BundleFileEntry Entry { get; set; }

            public string ExporterKey { get; set; }

            public bool Disposed { get; set; }

            public void Dispose()
            {
                if (this.Disposed)
                    return;

                try
                {

                    if (!(RunProcess?.HasExited ?? true))
                        RunProcess?.Kill();

                    if (File.Exists(Path))
                        File.Delete(Path);

                    Console.WriteLine("Deleted temp file {0}", Path);

                }
                catch (Exception exc){
                    Console.WriteLine(exc.Message);
                }

                this.Disposed = true;
            }
        }

        private Dictionary<FileEntry, TempFile> TempFiles = new Dictionary<FileEntry, TempFile>();

        private UITimer Timer;

        public bool Disposed { get; set; }

        public FileViewerManager()
        {
            Timer = new UITimer { Interval = 30 };
            Timer.Elapsed += Update;
            //Timer.Start();
            string temp_path;

            try
            {

                if (!Directory.Exists(temp_path = Path.Combine(Path.GetTempPath(), Definitions.TempDir)))
                    Directory.CreateDirectory(temp_path);
                else
                {
                    foreach (string file in Directory.GetFiles(temp_path))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.Message);
            }
        }

        ~FileViewerManager()
        {
            this.Dispose();
        }

        private bool IsFileAvailable(string path)
        {
            try
            {
                using (FileStream str = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return str.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldDeleteFile(TempFile file)
        {
            return file.RunProcess != null && file.RunProcess.HasExited && IsFileAvailable(file.Path);
        }

        private void Update(object sender, EventArgs e)
        {
            if (TempFiles.Count == 0)
                return;

            List<FileEntry> to_delete = new List<FileEntry>();
            foreach (KeyValuePair<FileEntry, TempFile> temp in TempFiles)
            {
                if (this.IsFileAvailable(temp.Value.Path))
                    to_delete.Add(temp.Key);
            }

            foreach (FileEntry ent in to_delete)
            {
                this.DeleteTempFile(ent);
            }
        }

        public void ViewFile(FileEntry entry, BundleFileEntry be = null)
        {
            try
            {
                string typ = Definitions.TypeFromExtension(entry._extension.ToString());
                dynamic exporter = null;

                if (ScriptActions.Converters.ContainsKey(typ))
                {
                    SaveOptionsDialog dlg = new SaveOptionsDialog(typ);
                    DialogResult dlgres = dlg.ShowModal();

                    if (dlgres == DialogResult.Cancel)
                        return;
                    exporter = dlg.SelectedExporter;
                }               

                //Thread thread = new Thread(() =>
                //{
                    TempFile temp = this.GetTempFile(entry, be, exporter);
                    //{
                        ProcessStartInfo pi = new ProcessStartInfo(temp.Path);
                        pi.UseShellExecute = true;
                        if (General.IsLinux)
                        {
                            pi.Arguments = temp.Path;
                            pi.FileName = "xdg-open";
                        }
                        else
                            pi.FileName = temp.Path;

                        Process proc = Process.Start(pi);
                        temp.RunProcess = proc;
                        if (!this.TempFiles.ContainsKey(entry))
                            this.TempFiles.Add(entry, temp);
                        /*if (proc == null)//seconds -> milliseconds
                            Thread.Sleep(20 * 1000);
                        proc?.WaitForExit();
                        while((!(proc?.HasExited ?? true)))
                        { }

                        if (General.IsLinux && (proc?.ExitCode == 3 || proc?.ExitCode == 4))
                            Console.WriteLine("No default file association for filetype {0}", Path.GetExtension(temp.Path));

                        while (!this.IsFileAvailable(temp.Path))
                        {
                            Console.WriteLine("Waiting on file");
                        }
                    }
                    this.TempFiles.Remove(entry);*/
                //});
                //thread.IsBackground = true;
                //thread.Start();

            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.Message);
                Console.WriteLine(exc.StackTrace);
            }
        }

        public TempFile CreateTempFile(FileEntry entry, BundleFileEntry be = null, dynamic exporter = null)
        {
            if (this.TempFiles.ContainsKey(entry))
                this.DeleteTempFile(entry);

            TempFile temp = new TempFile(entry, be, exporter);

            return temp;
        }

        public TempFile GetTempFile(FileEntry file, BundleFileEntry entry = null, dynamic exporter = null)
        {
            TempFile path;
            if (!this.TempFiles.ContainsKey(file) || TempFiles[file].Disposed || !File.Exists(TempFiles[file].Path) || TempFiles[file].ExporterKey != exporter?.key || TempFiles[file].Entry != entry)
            {
                if (this.TempFiles.ContainsKey(file))
                    this.DeleteTempFile(file);

                path = this.CreateTempFile(file, entry, exporter);
            }
            else
                path = this.TempFiles[file];

            return path;
        }

        public void DeleteTempFile(FileEntry entry)
        {
            if (!this.TempFiles.ContainsKey(entry))
                return;

            TempFile temp_file = this.TempFiles[entry];

            temp_file.Dispose();

            this.TempFiles.Remove(entry);
        }

        public void DeleteAllTempFiles()
        {
            List<TempFile> key_list = TempFiles.Values.ToList();
            for (int i = 0; i < this.TempFiles.Count; i++)
            {
                    key_list[i].Dispose();
            }

            TempFiles = new Dictionary<FileEntry, TempFile>();
        }

        public void Dispose()
        {
            if (this.Disposed)
                return;

            this.DeleteAllTempFiles();

            this.Disposed = true;
        }
    }
}