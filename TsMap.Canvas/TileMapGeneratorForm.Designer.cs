﻿namespace TsMap.Canvas
{
    partial class TileMapGeneratorForm
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
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.TreeNode treeNode8 = new System.Windows.Forms.TreeNode("Generate TileMap Info");
            System.Windows.Forms.TreeNode treeNode9 = new System.Windows.Forms.TreeNode("Localized Names");
            System.Windows.Forms.TreeNode treeNode10 = new System.Windows.Forms.TreeNode("Generate City List", new System.Windows.Forms.TreeNode[] {
            treeNode9});
            System.Windows.Forms.TreeNode treeNode11 = new System.Windows.Forms.TreeNode("Localized Names");
            System.Windows.Forms.TreeNode treeNode12 = new System.Windows.Forms.TreeNode("Generate Country List", new System.Windows.Forms.TreeNode[] {
            treeNode11});
            System.Windows.Forms.TreeNode treeNode13 = new System.Windows.Forms.TreeNode("Export As PNG");
            System.Windows.Forms.TreeNode treeNode14 = new System.Windows.Forms.TreeNode("Generate Overlay List", new System.Windows.Forms.TreeNode[] {
            treeNode13});
            this.GenerateBtn = new System.Windows.Forms.Button();
            this.StartLabel = new System.Windows.Forms.Label();
            this.EndLabel = new System.Windows.Forms.Label();
            this.StartZoomLevelBox = new System.Windows.Forms.NumericUpDown();
            this.EndZoomLevelBox = new System.Windows.Forms.NumericUpDown();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.CityNamesCheckBox = new System.Windows.Forms.CheckBox();
            this.MapAreasCheckBox = new System.Windows.Forms.CheckBox();
            this.FerryConnectionsCheckBox = new System.Windows.Forms.CheckBox();
            this.RoadsCheckBox = new System.Windows.Forms.CheckBox();
            this.MapOverlaysCheckBox = new System.Windows.Forms.CheckBox();
            this.PrefabsCheckBox = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.GenTilesCheck = new System.Windows.Forms.CheckBox();
            this.txtTileSize = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.lblMapPadding = new System.Windows.Forms.Label();
            this.txtMapPadding = new System.Windows.Forms.TextBox();
            this.triStateTreeView1 = new TsMap.Canvas.TriStateTreeView();
            this.SaveSettingsButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.StartZoomLevelBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.EndZoomLevelBox)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // GenerateBtn
            // 
            this.GenerateBtn.Location = new System.Drawing.Point(9, 240);
            this.GenerateBtn.Name = "GenerateBtn";
            this.GenerateBtn.Size = new System.Drawing.Size(298, 23);
            this.GenerateBtn.TabIndex = 2;
            this.GenerateBtn.Text = "Generate";
            this.GenerateBtn.UseVisualStyleBackColor = true;
            this.GenerateBtn.Click += new System.EventHandler(this.GenerateBtn_Click);
            // 
            // StartLabel
            // 
            this.StartLabel.AutoSize = true;
            this.StartLabel.Location = new System.Drawing.Point(7, 35);
            this.StartLabel.Name = "StartLabel";
            this.StartLabel.Size = new System.Drawing.Size(29, 13);
            this.StartLabel.TabIndex = 4;
            this.StartLabel.Text = "Start";
            // 
            // EndLabel
            // 
            this.EndLabel.AutoSize = true;
            this.EndLabel.Location = new System.Drawing.Point(97, 35);
            this.EndLabel.Name = "EndLabel";
            this.EndLabel.Size = new System.Drawing.Size(26, 13);
            this.EndLabel.TabIndex = 4;
            this.EndLabel.Text = "End";
            // 
            // StartZoomLevelBox
            // 
            this.StartZoomLevelBox.Location = new System.Drawing.Point(44, 33);
            this.StartZoomLevelBox.Maximum = new decimal(new int[] {
            18,
            0,
            0,
            0});
            this.StartZoomLevelBox.Name = "StartZoomLevelBox";
            this.StartZoomLevelBox.Size = new System.Drawing.Size(45, 20);
            this.StartZoomLevelBox.TabIndex = 0;
            // 
            // EndZoomLevelBox
            // 
            this.EndZoomLevelBox.Location = new System.Drawing.Point(131, 33);
            this.EndZoomLevelBox.Maximum = new decimal(new int[] {
            18,
            0,
            0,
            0});
            this.EndZoomLevelBox.Name = "EndZoomLevelBox";
            this.EndZoomLevelBox.Size = new System.Drawing.Size(45, 20);
            this.EndZoomLevelBox.TabIndex = 1;
            this.EndZoomLevelBox.Value = new decimal(new int[] {
            8,
            0,
            0,
            0});
            // 
            // CityNamesCheckBox
            // 
            this.CityNamesCheckBox.AutoSize = true;
            this.CityNamesCheckBox.Checked = true;
            this.CityNamesCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.CityNamesCheckBox.Location = new System.Drawing.Point(15, 143);
            this.CityNamesCheckBox.Name = "CityNamesCheckBox";
            this.CityNamesCheckBox.Size = new System.Drawing.Size(76, 17);
            this.CityNamesCheckBox.TabIndex = 11;
            this.CityNamesCheckBox.Text = "CityNames";
            this.CityNamesCheckBox.UseVisualStyleBackColor = true;
            // 
            // MapAreasCheckBox
            // 
            this.MapAreasCheckBox.AutoSize = true;
            this.MapAreasCheckBox.Checked = true;
            this.MapAreasCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.MapAreasCheckBox.Location = new System.Drawing.Point(15, 74);
            this.MapAreasCheckBox.Name = "MapAreasCheckBox";
            this.MapAreasCheckBox.Size = new System.Drawing.Size(74, 17);
            this.MapAreasCheckBox.TabIndex = 8;
            this.MapAreasCheckBox.Text = "MapAreas";
            this.MapAreasCheckBox.UseVisualStyleBackColor = true;
            // 
            // FerryConnectionsCheckBox
            // 
            this.FerryConnectionsCheckBox.AutoSize = true;
            this.FerryConnectionsCheckBox.Checked = true;
            this.FerryConnectionsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.FerryConnectionsCheckBox.Location = new System.Drawing.Point(15, 120);
            this.FerryConnectionsCheckBox.Name = "FerryConnectionsCheckBox";
            this.FerryConnectionsCheckBox.Size = new System.Drawing.Size(108, 17);
            this.FerryConnectionsCheckBox.TabIndex = 10;
            this.FerryConnectionsCheckBox.Text = "FerryConnections";
            this.FerryConnectionsCheckBox.UseVisualStyleBackColor = true;
            // 
            // RoadsCheckBox
            // 
            this.RoadsCheckBox.AutoSize = true;
            this.RoadsCheckBox.Checked = true;
            this.RoadsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.RoadsCheckBox.Location = new System.Drawing.Point(15, 51);
            this.RoadsCheckBox.Name = "RoadsCheckBox";
            this.RoadsCheckBox.Size = new System.Drawing.Size(57, 17);
            this.RoadsCheckBox.TabIndex = 7;
            this.RoadsCheckBox.Text = "Roads";
            this.RoadsCheckBox.UseVisualStyleBackColor = true;
            // 
            // MapOverlaysCheckBox
            // 
            this.MapOverlaysCheckBox.AutoSize = true;
            this.MapOverlaysCheckBox.Checked = true;
            this.MapOverlaysCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.MapOverlaysCheckBox.Location = new System.Drawing.Point(15, 97);
            this.MapOverlaysCheckBox.Name = "MapOverlaysCheckBox";
            this.MapOverlaysCheckBox.Size = new System.Drawing.Size(88, 17);
            this.MapOverlaysCheckBox.TabIndex = 9;
            this.MapOverlaysCheckBox.Text = "MapOverlays";
            this.MapOverlaysCheckBox.UseVisualStyleBackColor = true;
            // 
            // PrefabsCheckBox
            // 
            this.PrefabsCheckBox.AutoSize = true;
            this.PrefabsCheckBox.Checked = true;
            this.PrefabsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.PrefabsCheckBox.Location = new System.Drawing.Point(15, 28);
            this.PrefabsCheckBox.Name = "PrefabsCheckBox";
            this.PrefabsCheckBox.Size = new System.Drawing.Size(62, 17);
            this.PrefabsCheckBox.TabIndex = 6;
            this.PrefabsCheckBox.Text = "Prefabs";
            this.PrefabsCheckBox.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.PrefabsCheckBox);
            this.groupBox1.Controls.Add(this.CityNamesCheckBox);
            this.groupBox1.Controls.Add(this.MapOverlaysCheckBox);
            this.groupBox1.Controls.Add(this.MapAreasCheckBox);
            this.groupBox1.Controls.Add(this.RoadsCheckBox);
            this.groupBox1.Controls.Add(this.FerryConnectionsCheckBox);
            this.groupBox1.Location = new System.Drawing.Point(183, 7);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(125, 172);
            this.groupBox1.TabIndex = 12;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Items To Render";
            // 
            // GenTilesCheck
            // 
            this.GenTilesCheck.AutoSize = true;
            this.GenTilesCheck.Checked = true;
            this.GenTilesCheck.CheckState = System.Windows.Forms.CheckState.Checked;
            this.GenTilesCheck.Location = new System.Drawing.Point(10, 12);
            this.GenTilesCheck.Name = "GenTilesCheck";
            this.GenTilesCheck.Size = new System.Drawing.Size(95, 17);
            this.GenTilesCheck.TabIndex = 5;
            this.GenTilesCheck.Text = "Generate Tiles";
            this.GenTilesCheck.UseVisualStyleBackColor = true;
            this.GenTilesCheck.CheckedChanged += new System.EventHandler(this.GenTilesCheck_CheckedChanged);
            // 
            // txtTileSize
            // 
            this.txtTileSize.Location = new System.Drawing.Point(60, 185);
            this.txtTileSize.Name = "txtTileSize";
            this.txtTileSize.Size = new System.Drawing.Size(63, 20);
            this.txtTileSize.TabIndex = 14;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(7, 188);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(47, 13);
            this.label1.TabIndex = 15;
            this.label1.Text = "Tile Size";
            // 
            // lblMapPadding
            // 
            this.lblMapPadding.AutoSize = true;
            this.lblMapPadding.Location = new System.Drawing.Point(162, 188);
            this.lblMapPadding.Name = "lblMapPadding";
            this.lblMapPadding.Size = new System.Drawing.Size(70, 13);
            this.lblMapPadding.TabIndex = 16;
            this.lblMapPadding.Text = "Map Padding";
            // 
            // txtMapPadding
            // 
            this.txtMapPadding.Location = new System.Drawing.Point(238, 185);
            this.txtMapPadding.Name = "txtMapPadding";
            this.txtMapPadding.Size = new System.Drawing.Size(69, 20);
            this.txtMapPadding.TabIndex = 17;
            // 
            // triStateTreeView1
            // 
            this.triStateTreeView1.Location = new System.Drawing.Point(10, 59);
            this.triStateTreeView1.Name = "triStateTreeView1";
            treeNode8.Checked = true;
            treeNode8.Name = "GenTileMapInfo";
            treeNode8.StateImageIndex = 1;
            treeNode8.Text = "Generate TileMap Info";
            treeNode9.Name = "GenCityLocalizedNames";
            treeNode9.StateImageIndex = 0;
            treeNode9.Text = "Localized Names";
            treeNode10.Name = "GenCityList";
            treeNode10.StateImageIndex = 0;
            treeNode10.Text = "Generate City List";
            treeNode11.Name = "GenCountryLocalizedNames";
            treeNode11.StateImageIndex = 0;
            treeNode11.Text = "Localized Names";
            treeNode12.Name = "GenCountryList";
            treeNode12.StateImageIndex = 0;
            treeNode12.Text = "Generate Country List";
            treeNode13.Name = "GenOverlayPNGs";
            treeNode13.StateImageIndex = 0;
            treeNode13.Text = "Export As PNG";
            treeNode14.Name = "GenOverlayList";
            treeNode14.StateImageIndex = 0;
            treeNode14.Text = "Generate Overlay List";
            this.triStateTreeView1.Nodes.AddRange(new System.Windows.Forms.TreeNode[] {
            treeNode8,
            treeNode10,
            treeNode12,
            treeNode14});
            this.triStateTreeView1.Size = new System.Drawing.Size(166, 120);
            this.triStateTreeView1.TabIndex = 13;
            // 
            // SaveSettingsButton
            // 
            this.SaveSettingsButton.Location = new System.Drawing.Point(8, 211);
            this.SaveSettingsButton.Name = "SaveSettingsButton";
            this.SaveSettingsButton.Size = new System.Drawing.Size(298, 23);
            this.SaveSettingsButton.TabIndex = 18;
            this.SaveSettingsButton.Text = "Save Settings";
            this.SaveSettingsButton.UseVisualStyleBackColor = true;
            this.SaveSettingsButton.Click += new System.EventHandler(this.SaveSettings_Click);
            // 
            // TileMapGeneratorForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(319, 271);
            this.Controls.Add(this.SaveSettingsButton);
            this.Controls.Add(this.txtMapPadding);
            this.Controls.Add(this.lblMapPadding);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtTileSize);
            this.Controls.Add(this.triStateTreeView1);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.GenTilesCheck);
            this.Controls.Add(this.EndZoomLevelBox);
            this.Controls.Add(this.StartZoomLevelBox);
            this.Controls.Add(this.EndLabel);
            this.Controls.Add(this.StartLabel);
            this.Controls.Add(this.GenerateBtn);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "TileMapGeneratorForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Export as Web Tile Map";
            ((System.ComponentModel.ISupportInitialize)(this.StartZoomLevelBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.EndZoomLevelBox)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button GenerateBtn;
        private System.Windows.Forms.Label StartLabel;
        private System.Windows.Forms.Label EndLabel;
        private System.Windows.Forms.NumericUpDown StartZoomLevelBox;
        private System.Windows.Forms.NumericUpDown EndZoomLevelBox;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.CheckBox CityNamesCheckBox;
        private System.Windows.Forms.CheckBox MapAreasCheckBox;
        private System.Windows.Forms.CheckBox FerryConnectionsCheckBox;
        private System.Windows.Forms.CheckBox RoadsCheckBox;
        private System.Windows.Forms.CheckBox MapOverlaysCheckBox;
        private System.Windows.Forms.CheckBox PrefabsCheckBox;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.CheckBox GenTilesCheck;
        private TriStateTreeView triStateTreeView1;
        private System.Windows.Forms.TextBox txtTileSize;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblMapPadding;
        private System.Windows.Forms.TextBox txtMapPadding;
        private System.Windows.Forms.Button SaveSettingsButton;
    }
}