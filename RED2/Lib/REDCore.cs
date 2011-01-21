﻿using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RED2
{
    public class REDCore
    {
        private REDWorkflowSteps currentProcessStep = REDWorkflowSteps.Init;

        private CalculateDirectoryCountWorker calcDirCountWorker = null;
        private FindEmptyDirectoryWorker searchEmptyFoldersWorker = null;

        public event EventHandler<REDCoreWorkflowStepChangedEventArgs> OnWorkflowStepChanged;
        public event EventHandler<REDCoreErrorEventArgs> OnError;
        public event EventHandler<REDCoreCalcDirWorkerFinishedEventArgs> OnCalcDirWorkerFinished;
        public event EventHandler OnCancelled;
        public event EventHandler<ProgressChangedEventArgs> OnProgressChanged;
        public event EventHandler<REDCoreFoundDirEventArgs> OnFoundEmptyDir;
        public event EventHandler<REDCoreFinishedScanForEmptyDirsEventArgs> OnFinishedScanForEmptyDirs;
        public event EventHandler<REDCoreDeleteProcessUpdateEventArgs> OnDeleteProcessChanged;
        public event EventHandler<REDCoreDeleteProcessFinishedEventArgs> OnDeleteProcessFinished;

        public bool SimulateDeletion { get; set; }

        private List<DirectoryInfo> emptyFolderList = null;

        private bool stopDeleteProcessTrigger = false;
        private Dictionary<String, String> protectedFolderList = new Dictionary<string, string>();

        public REDCore()
        {
            this.SimulateDeletion = false;
        }

        public void init()
        {

            protectedFolderList = new Dictionary<string, string>();
            this.set_step(REDWorkflowSteps.Init);
        }

        public void step1_calcDirectoryCount(DirectoryInfo Folder, int MaxDepth)
        {
            emptyFolderList = new List<DirectoryInfo>();

            this.set_step(REDWorkflowSteps.StartingCalcDirCount);

            // Create new blank worker
            this.calcDirCountWorker = new CalculateDirectoryCountWorker();
            this.calcDirCountWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FSWorker_RunWorkerCompleted);
            this.calcDirCountWorker.MaxDepth = MaxDepth;
            this.calcDirCountWorker.RunWorkerAsync(Folder);
        }

        private void set_step(REDWorkflowSteps step)
        {
            if (this.OnWorkflowStepChanged != null)
                this.OnWorkflowStepChanged(this, new REDCoreWorkflowStepChangedEventArgs(step));
        }

        /// <summary>
        /// Scan process finished
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FSWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            CalculateDirectoryCountWorker LWorker = sender as CalculateDirectoryCountWorker;

            calcDirCountWorker.Dispose();
            calcDirCountWorker = null;

            // First, handle the case where an exception was thrown.
            if (e.Error != null)
            {
                showErrorMsg(e.Error.Message);
            }
            else if (e.Cancelled)
            {
                if (this.OnCancelled != null)
                    this.OnCancelled(this, new EventArgs());
            }
            else
            {
                if (this.OnCalcDirWorkerFinished != null)
                    this.OnCalcDirWorkerFinished(this, new REDCoreCalcDirWorkerFinishedEventArgs((int)e.Result));
            }
        }

        private void showErrorMsg(string errorMessage)
        {
            if (this.OnError != null)
                this.OnError(this, new REDCoreErrorEventArgs(errorMessage));
        }


        /// <summary>
        /// Start searching empty folders
        /// </summary>
        public void step2_startSearchingEmptyDirectories(DirectoryInfo StartFolder, string tbIgnoreFiles, string tbIgnoreFolders, bool cbIgnore0kbFiles, bool cbIgnoreHiddenFolders, bool cbKeepSystemFolders, int nuMaxDepth)
        {
            #region Initialize the FolderFindWorker-Object

            this.emptyFolderList = new List<DirectoryInfo>();

            this.set_step(REDWorkflowSteps.StartSearchingForEmptyDirs);

            searchEmptyFoldersWorker = new FindEmptyDirectoryWorker();
            searchEmptyFoldersWorker.ProgressChanged += new ProgressChangedEventHandler(FFWorker_ProgressChanged);
            searchEmptyFoldersWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FFWorker_RunWorkerCompleted);

            // set options:
            if (!searchEmptyFoldersWorker.SetIgnoreFiles(tbIgnoreFiles))
            {
                showErrorMsg(RED2.Properties.Resources.error_ignore_settings);
                return;
            }

            if (!searchEmptyFoldersWorker.SetIgnoreFolders(tbIgnoreFolders))
            {
                showErrorMsg(RED2.Properties.Resources.error_ignore_settings);
                return;
            }

            searchEmptyFoldersWorker.Ignore0kbFiles = cbIgnore0kbFiles;
            searchEmptyFoldersWorker.IgnoreHiddenFolders = cbIgnoreHiddenFolders;
            searchEmptyFoldersWorker.IgnoreSystemFolders = cbKeepSystemFolders;

            searchEmptyFoldersWorker.MaxDepth = nuMaxDepth;

            // Start worker
            searchEmptyFoldersWorker.RunWorkerAsync(StartFolder);

            #endregion

        }

        /// <summary>
        /// This function gets called on a status update of the 
        /// find worker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void FFWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if ((int)e.ProgressPercentage != -1)
            {
                if (this.OnProgressChanged != null)
                    this.OnProgressChanged(this, e);
            }
            else
            {
                var directory = (DirectoryInfo)e.UserState;

                emptyFolderList.Add(directory);

                if (this.OnFoundEmptyDir != null)
                    this.OnFoundEmptyDir(this, new REDCoreFoundDirEventArgs(directory));
            }
        }

        void FFWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FindEmptyDirectoryWorker FindWorker = sender as FindEmptyDirectoryWorker;

            if (e.Error != null)
            {
                this.showErrorMsg(e.Error.Message);

                searchEmptyFoldersWorker.Dispose();
                searchEmptyFoldersWorker = null;
                return;
            }
            else if (e.Cancelled)
            {
                if (this.OnCancelled != null)
                    this.OnCancelled(this, new EventArgs());
            }
            else
            {
                int EmptyFolderCount = FindWorker.EmptyFolderCount;
                int FolderCount = FindWorker.FolderCount;

                searchEmptyFoldersWorker.Dispose();
                searchEmptyFoldersWorker = null;

                if (this.OnFinishedScanForEmptyDirs != null)
                    this.OnFinishedScanForEmptyDirs(this, new REDCoreFinishedScanForEmptyDirsEventArgs(EmptyFolderCount, FolderCount));
            }
        }


        internal void cancelDirCountWorker()
        {
            if (this.calcDirCountWorker == null) return;

            if ((this.calcDirCountWorker.IsBusy == true) || (calcDirCountWorker.CancellationPending == false))
                calcDirCountWorker.CancelAsync();
        }

        internal void cancelSearchEmptyFolderWorker()
        {
            if (this.searchEmptyFoldersWorker == null) return;

            if ((this.searchEmptyFoldersWorker.IsBusy == true) || (searchEmptyFoldersWorker.CancellationPending == false))
                searchEmptyFoldersWorker.CancelAsync();
        }


        /// <summary>
        /// Finally delete a folder (with security checks before)
        /// </summary>
        /// <param name="_StartFolder"></param>
        /// <returns></returns>
        public bool SecureDelete(DirectoryInfo _StartFolder, String[] ignoreFiles, bool cbIgnore0kbFiles)
        {
            if (this.SimulateDeletion)
                return true;

            if (!Directory.Exists(_StartFolder.FullName))
                return false;


            // Cleanup folder

            FileInfo[] Files = _StartFolder.GetFiles();

            if (Files != null && Files.Length != 0)
            {
                // loop trough files and cancel if containsFiles == true
                for (int f = 0; f < Files.Length; f++)
                {
                    FileInfo file = Files[f];

                    bool matches_a_pattern = false;

                    for (int p = 0; (p < ignoreFiles.Length && !matches_a_pattern); p++)
                    {
                        string pattern = ignoreFiles[p];

                        if (cbIgnore0kbFiles && file.Length == 0)
                            matches_a_pattern = true;
                        else if (pattern.ToLower() == file.Name.ToLower())
                            matches_a_pattern = true;
                        else if (pattern.Contains("*"))
                        {
                            pattern = Regex.Escape(pattern);
                            pattern = pattern.Replace("\\*", ".*");

                            Regex RgxPattern = new Regex("^" + pattern + "$");

                            if (RgxPattern.IsMatch(file.Name))
                                matches_a_pattern = true;
                        }
                        else if (pattern.StartsWith("/") && pattern.EndsWith("/"))
                        {
                            Regex RgxPattern = new Regex(pattern.Substring(1, pattern.Length - 2));

                            if (RgxPattern.IsMatch(file.Name))
                                matches_a_pattern = true;
                        }

                    }

                    // If only one file is good, then stop.
                    if (matches_a_pattern)
                        file.Delete();

                }
            }

            // End cleanup

            return SystemFunctions.SecureDelete(_StartFolder);
        }

        internal void CancelProcess()
        {
            if (this.currentProcessStep == REDWorkflowSteps.StartingCalcDirCount)
            {
                this.cancelDirCountWorker();
            }
            else if (this.currentProcessStep == REDWorkflowSteps.StartSearchingForEmptyDirs)
            {
                this.cancelSearchEmptyFolderWorker();
            }
            else if (this.currentProcessStep == REDWorkflowSteps.DeleteProcessRunning)
            {
                this.stopDeleteProcessTrigger = true;
            }
        }

        internal void StartDelete(String[] ignoreFileList, bool cbIgnore0kbFiles, double pauseTime)
        {
            this.set_step(REDWorkflowSteps.DeleteProcessRunning);

            this.stopDeleteProcessTrigger = false;

            int DeletedFolderCount = 0;
            int FailedFolderCount = 0;

            int FolderCount = emptyFolderList.Count;

            for (int i = 0; (i < emptyFolderList.Count && !this.stopDeleteProcessTrigger); i++)
            {
                DirectoryInfo Folder = emptyFolderList[i];
                REDDirStatus status = REDDirStatus.Ignored;

                // Do not delete protected folders:
                if (!this.protectedFolderList.ContainsKey(Folder.FullName))
                {
                    if (this.SecureDelete(Folder, ignoreFileList, cbIgnore0kbFiles))
                    {
                        status = REDDirStatus.Deleted;
                        DeletedFolderCount++;
                    }
                    else
                    {
                        status = REDDirStatus.Warning;
                        FailedFolderCount++;
                    }

                    // Hack to allow the user to cancel the deletion process
                    Application.DoEvents();
                    Thread.Sleep(TimeSpan.FromMilliseconds(pauseTime));

                }
                else
                    status = REDDirStatus.Protected;

                if (this.OnDeleteProcessChanged != null)
                    this.OnDeleteProcessChanged(this, new REDCoreDeleteProcessUpdateEventArgs(i, Folder, status, FolderCount));

            }

            if (this.OnDeleteProcessFinished != null)
                this.OnDeleteProcessFinished(this, new REDCoreDeleteProcessFinishedEventArgs(DeletedFolderCount, FailedFolderCount));

        }

        internal void AddProtectedFolder(string FullName, string Key)
        {
            if (!this.protectedFolderList.ContainsKey(FullName))
                this.protectedFolderList.Add(FullName, Key);

        }

        internal void RemoveProtected(string FolderFullName)
        {
            this.protectedFolderList.Remove(FolderFullName);

        }

        internal bool ProtectedContainsKey(string FolderFullName)
        {
            return this.protectedFolderList.ContainsKey(FolderFullName);
        }

        internal string[] GetProtectedNode(string selectedFolderFullName)
        {
            return ((string)this.protectedFolderList[selectedFolderFullName]).Split('|');
        }
    }
}