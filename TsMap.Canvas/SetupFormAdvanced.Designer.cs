
namespace TsMap.Canvas
{
    partial class SetupFormAdvanced
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.cmbMapSets = new System.Windows.Forms.ComboBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtMapSetName = new System.Windows.Forms.TextBox();
            this.chkLoadMods = new System.Windows.Forms.CheckBox();
            this.txtFileFilters = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.btnBrowseFolder = new System.Windows.Forms.Button();
            this.txtPath = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.fldBrwDlgGamePath = new System.Windows.Forms.FolderBrowserDialog();
            this.grpMods = new System.Windows.Forms.GroupBox();
            this.btnDecreasePriority = new System.Windows.Forms.Button();
            this.btnIncreasePriority = new System.Windows.Forms.Button();
            this.btnRemoveMod = new System.Windows.Forms.Button();
            this.btnSelectMod = new System.Windows.Forms.Button();
            this.lstSelectedMods = new System.Windows.Forms.ListBox();
            this.lstMods = new System.Windows.Forms.ListBox();
            this.btnSelectModsFolder = new System.Windows.Forms.Button();
            this.btnSaveMapSet = new System.Windows.Forms.Button();
            this.btnContinue = new System.Windows.Forms.Button();
            this.btnNewMapSet = new System.Windows.Forms.Button();
            this.folderBrowserDialogModFolder = new System.Windows.Forms.FolderBrowserDialog();
            this.label5 = new System.Windows.Forms.Label();
            this.txtOutputPath = new System.Windows.Forms.TextBox();
            this.btnBrowseOutputPath = new System.Windows.Forms.Button();
            this.folderBrowserDialogOutputPath = new System.Windows.Forms.FolderBrowserDialog();
            this.groupBox1.SuspendLayout();
            this.grpMods.SuspendLayout();
            this.SuspendLayout();
            // 
            // cmbMapSets
            // 
            this.cmbMapSets.FormattingEnabled = true;
            this.cmbMapSets.Location = new System.Drawing.Point(162, 6);
            this.cmbMapSets.Name = "cmbMapSets";
            this.cmbMapSets.Size = new System.Drawing.Size(121, 21);
            this.cmbMapSets.TabIndex = 0;
            this.cmbMapSets.Text = "Select a Map Set";
            this.cmbMapSets.SelectedIndexChanged += new System.EventHandler(this.cmbMapSets_SelectedIndexChanged);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.btnBrowseOutputPath);
            this.groupBox1.Controls.Add(this.txtOutputPath);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.txtMapSetName);
            this.groupBox1.Controls.Add(this.chkLoadMods);
            this.groupBox1.Controls.Add(this.txtFileFilters);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.btnBrowseFolder);
            this.groupBox1.Controls.Add(this.txtPath);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Location = new System.Drawing.Point(12, 33);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(586, 168);
            this.groupBox1.TabIndex = 2;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Map Set";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(6, 23);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(35, 13);
            this.label4.TabIndex = 10;
            this.label4.Text = "Name";
            // 
            // txtMapSetName
            // 
            this.txtMapSetName.Location = new System.Drawing.Point(150, 20);
            this.txtMapSetName.Name = "txtMapSetName";
            this.txtMapSetName.Size = new System.Drawing.Size(211, 20);
            this.txtMapSetName.TabIndex = 9;
            // 
            // chkLoadMods
            // 
            this.chkLoadMods.AutoSize = true;
            this.chkLoadMods.Location = new System.Drawing.Point(150, 98);
            this.chkLoadMods.Name = "chkLoadMods";
            this.chkLoadMods.Size = new System.Drawing.Size(79, 17);
            this.chkLoadMods.TabIndex = 8;
            this.chkLoadMods.Text = "Load Mods";
            this.chkLoadMods.UseVisualStyleBackColor = true;
            this.chkLoadMods.CheckedChanged += new System.EventHandler(this.ChkLoadMods_CheckedChanged);
            // 
            // txtFileFilters
            // 
            this.txtFileFilters.Location = new System.Drawing.Point(150, 72);
            this.txtFileFilters.Name = "txtFileFilters";
            this.txtFileFilters.Size = new System.Drawing.Size(211, 20);
            this.txtFileFilters.TabIndex = 7;
            this.txtFileFilters.Text = "*.scs";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(6, 75);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(102, 13);
            this.label3.TabIndex = 6;
            this.label3.Text = "Files Filter (es. *.scs)";
            // 
            // btnBrowseFolder
            // 
            this.btnBrowseFolder.Location = new System.Drawing.Point(501, 44);
            this.btnBrowseFolder.Name = "btnBrowseFolder";
            this.btnBrowseFolder.Size = new System.Drawing.Size(79, 23);
            this.btnBrowseFolder.TabIndex = 5;
            this.btnBrowseFolder.Text = "Browse";
            this.btnBrowseFolder.UseVisualStyleBackColor = true;
            this.btnBrowseFolder.Click += new System.EventHandler(this.BtnBrowseFolder_Click);
            // 
            // txtPath
            // 
            this.txtPath.Location = new System.Drawing.Point(150, 46);
            this.txtPath.Name = "txtPath";
            this.txtPath.Size = new System.Drawing.Size(345, 20);
            this.txtPath.TabIndex = 4;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 49);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(29, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Path";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(52, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Map Sets";
            // 
            // grpMods
            // 
            this.grpMods.Controls.Add(this.btnDecreasePriority);
            this.grpMods.Controls.Add(this.btnIncreasePriority);
            this.grpMods.Controls.Add(this.btnRemoveMod);
            this.grpMods.Controls.Add(this.btnSelectMod);
            this.grpMods.Controls.Add(this.lstSelectedMods);
            this.grpMods.Controls.Add(this.lstMods);
            this.grpMods.Controls.Add(this.btnSelectModsFolder);
            this.grpMods.Location = new System.Drawing.Point(12, 207);
            this.grpMods.Name = "grpMods";
            this.grpMods.Size = new System.Drawing.Size(586, 278);
            this.grpMods.TabIndex = 3;
            this.grpMods.TabStop = false;
            this.grpMods.Text = "Mods";
            this.grpMods.Visible = false;
            // 
            // btnDecreasePriority
            // 
            this.btnDecreasePriority.Location = new System.Drawing.Point(235, 162);
            this.btnDecreasePriority.Name = "btnDecreasePriority";
            this.btnDecreasePriority.Size = new System.Drawing.Size(91, 23);
            this.btnDecreasePriority.TabIndex = 16;
            this.btnDecreasePriority.Text = "- Priority";
            this.btnDecreasePriority.UseVisualStyleBackColor = true;
            this.btnDecreasePriority.Click += new System.EventHandler(this.BtnDecreasePriority_Click);
            // 
            // btnIncreasePriority
            // 
            this.btnIncreasePriority.Location = new System.Drawing.Point(235, 133);
            this.btnIncreasePriority.Name = "btnIncreasePriority";
            this.btnIncreasePriority.Size = new System.Drawing.Size(91, 23);
            this.btnIncreasePriority.TabIndex = 15;
            this.btnIncreasePriority.Text = "+ Priority";
            this.btnIncreasePriority.UseVisualStyleBackColor = true;
            this.btnIncreasePriority.Click += new System.EventHandler(this.BtnIncreasePriority_Click);
            // 
            // btnRemoveMod
            // 
            this.btnRemoveMod.Location = new System.Drawing.Point(235, 77);
            this.btnRemoveMod.Name = "btnRemoveMod";
            this.btnRemoveMod.Size = new System.Drawing.Size(91, 23);
            this.btnRemoveMod.TabIndex = 14;
            this.btnRemoveMod.Text = "<";
            this.btnRemoveMod.UseVisualStyleBackColor = true;
            this.btnRemoveMod.Click += new System.EventHandler(this.BtnRemoveMod_Click);
            // 
            // btnSelectMod
            // 
            this.btnSelectMod.Location = new System.Drawing.Point(235, 48);
            this.btnSelectMod.Name = "btnSelectMod";
            this.btnSelectMod.Size = new System.Drawing.Size(91, 23);
            this.btnSelectMod.TabIndex = 7;
            this.btnSelectMod.Text = ">";
            this.btnSelectMod.UseVisualStyleBackColor = true;
            this.btnSelectMod.Click += new System.EventHandler(this.BtnSelectMod_Click);
            // 
            // lstSelectedMods
            // 
            this.lstSelectedMods.FormattingEnabled = true;
            this.lstSelectedMods.Location = new System.Drawing.Point(332, 47);
            this.lstSelectedMods.Name = "lstSelectedMods";
            this.lstSelectedMods.Size = new System.Drawing.Size(248, 225);
            this.lstSelectedMods.TabIndex = 13;
            this.lstSelectedMods.DoubleClick += new System.EventHandler(this.LstSelectedMods_DoubleClick);
            // 
            // lstMods
            // 
            this.lstMods.FormattingEnabled = true;
            this.lstMods.Location = new System.Drawing.Point(6, 48);
            this.lstMods.Name = "lstMods";
            this.lstMods.Size = new System.Drawing.Size(223, 225);
            this.lstMods.TabIndex = 12;
            this.lstMods.DoubleClick += new System.EventHandler(this.LstMods_DoubleClick);
            // 
            // btnSelectModsFolder
            // 
            this.btnSelectModsFolder.Location = new System.Drawing.Point(6, 19);
            this.btnSelectModsFolder.Name = "btnSelectModsFolder";
            this.btnSelectModsFolder.Size = new System.Drawing.Size(223, 23);
            this.btnSelectModsFolder.TabIndex = 11;
            this.btnSelectModsFolder.Text = "Select Mods Folder";
            this.btnSelectModsFolder.UseVisualStyleBackColor = true;
            this.btnSelectModsFolder.Click += new System.EventHandler(this.BtnSelectModsFolder_Click);
            // 
            // btnSaveMapSet
            // 
            this.btnSaveMapSet.Location = new System.Drawing.Point(12, 491);
            this.btnSaveMapSet.Name = "btnSaveMapSet";
            this.btnSaveMapSet.Size = new System.Drawing.Size(105, 23);
            this.btnSaveMapSet.TabIndex = 4;
            this.btnSaveMapSet.Text = "Save Map Set";
            this.btnSaveMapSet.UseVisualStyleBackColor = true;
            this.btnSaveMapSet.Click += new System.EventHandler(this.btnSaveMapSet_Click);
            // 
            // btnContinue
            // 
            this.btnContinue.Location = new System.Drawing.Point(493, 491);
            this.btnContinue.Name = "btnContinue";
            this.btnContinue.Size = new System.Drawing.Size(105, 23);
            this.btnContinue.TabIndex = 5;
            this.btnContinue.Text = "Continue";
            this.btnContinue.UseVisualStyleBackColor = true;
            this.btnContinue.Click += new System.EventHandler(this.btnContinue_Click);
            // 
            // btnNewMapSet
            // 
            this.btnNewMapSet.Location = new System.Drawing.Point(289, 4);
            this.btnNewMapSet.Name = "btnNewMapSet";
            this.btnNewMapSet.Size = new System.Drawing.Size(105, 23);
            this.btnNewMapSet.TabIndex = 6;
            this.btnNewMapSet.Text = "New Map Set";
            this.btnNewMapSet.UseVisualStyleBackColor = true;
            this.btnNewMapSet.Click += new System.EventHandler(this.btnNewMapSet_Click);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(6, 122);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(64, 13);
            this.label5.TabIndex = 11;
            this.label5.Text = "Output Path";
            // 
            // txtOutputPath
            // 
            this.txtOutputPath.Location = new System.Drawing.Point(150, 119);
            this.txtOutputPath.Name = "txtOutputPath";
            this.txtOutputPath.Size = new System.Drawing.Size(345, 20);
            this.txtOutputPath.TabIndex = 12;
            // 
            // btnBrowseOutputPath
            // 
            this.btnBrowseOutputPath.Location = new System.Drawing.Point(501, 117);
            this.btnBrowseOutputPath.Name = "btnBrowseOutputPath";
            this.btnBrowseOutputPath.Size = new System.Drawing.Size(79, 23);
            this.btnBrowseOutputPath.TabIndex = 13;
            this.btnBrowseOutputPath.Text = "Browse";
            this.btnBrowseOutputPath.UseVisualStyleBackColor = true;
            this.btnBrowseOutputPath.Click += new System.EventHandler(this.btnBrowseOutputPath_Click);
            // 
            // SetupFormAdvanced
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(610, 523);
            this.Controls.Add(this.btnNewMapSet);
            this.Controls.Add(this.btnContinue);
            this.Controls.Add(this.btnSaveMapSet);
            this.Controls.Add(this.grpMods);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.cmbMapSets);
            this.Name = "SetupFormAdvanced";
            this.Text = "Setup - TSMap";
            this.Load += new System.EventHandler(this.SetupFormAdvanced_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.grpMods.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox cmbMapSets;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.CheckBox chkLoadMods;
        private System.Windows.Forms.TextBox txtFileFilters;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button btnBrowseFolder;
        private System.Windows.Forms.TextBox txtPath;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.FolderBrowserDialog fldBrwDlgGamePath;
        private System.Windows.Forms.GroupBox grpMods;
        private System.Windows.Forms.Button btnSaveMapSet;
        private System.Windows.Forms.Button btnContinue;
        private System.Windows.Forms.Button btnNewMapSet;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtMapSetName;
        private System.Windows.Forms.Button btnSelectModsFolder;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialogModFolder;
        private System.Windows.Forms.ListBox lstSelectedMods;
        private System.Windows.Forms.ListBox lstMods;
        private System.Windows.Forms.Button btnRemoveMod;
        private System.Windows.Forms.Button btnSelectMod;
        private System.Windows.Forms.Button btnDecreasePriority;
        private System.Windows.Forms.Button btnIncreasePriority;
        private System.Windows.Forms.Button btnBrowseOutputPath;
        private System.Windows.Forms.TextBox txtOutputPath;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialogOutputPath;
    }
}