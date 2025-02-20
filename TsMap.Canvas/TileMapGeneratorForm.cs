﻿using System;
using System.IO;
using System.Windows.Forms;

namespace TsMap.Canvas
{
    public partial class TileMapGeneratorForm : Form
    {
        public delegate void GenerateTileMapEvent();

        public GenerateTileMapEvent GenerateTileMap;
        public TileMapGeneratorForm()
        {
            InitializeComponent();
            folderBrowserDialog1.Description = "Select where you want the tile map files to be placed";
            folderBrowserDialog1.SelectedPath = SettingsManager.Current.Settings.TileGenerator.LastTileMapPath;

            PrefabsCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.Prefabs);
            RoadsCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.Roads);
            MapAreasCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.MapAreas);
            MapOverlaysCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.MapOverlays);
            FerryConnectionsCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.FerryConnections);
            CityNamesCheckBox.Checked = SettingsManager.Current.Settings.TileGenerator.RenderFlags.IsActive(RenderFlags.CityNames);

            EndZoomLevelBox.Value = SettingsManager.Current.Settings.TileGenerator.EndZoomLevel;
            StartZoomLevelBox.Value = SettingsManager.Current.Settings.TileGenerator.StartZoomLevel;
            GenTilesCheck.Checked = SettingsManager.Current.Settings.TileGenerator.GenerateTiles;

            txtMapPadding.Text = SettingsManager.Current.Settings.TileGenerator.MapPadding.ToString();
            txtTileSize.Text = SettingsManager.Current.Settings.TileGenerator.TileSize.ToString();

            SetExportFlags();

            triStateTreeView1.ItemChecked += (TreeNode node) =>
            {
                if (node.Name == "GenCityList")
                {
                    CityNamesCheckBox.Checked = !node.Checked;
                    triStateTreeView1.GetNodeByName("GenCountryList").Checked = node.Checked;
                    triStateTreeView1.GetNodeByName("GenCountryLocalizedNames").Checked = node.Checked;
                }
                else if (node.Name == "GenOverlayList") MapOverlaysCheckBox.Checked = !node.Checked;
            };
        }

        private RenderFlags GetRenderFlags()
        {
            RenderFlags renderFlags = 0;
            if (PrefabsCheckBox.Checked) renderFlags |= RenderFlags.Prefabs;
            if (RoadsCheckBox.Checked) renderFlags |= RenderFlags.Roads;
            if (MapAreasCheckBox.Checked) renderFlags |= RenderFlags.MapAreas;
            if (MapOverlaysCheckBox.Checked) renderFlags |= RenderFlags.MapOverlays;
            if (FerryConnectionsCheckBox.Checked) renderFlags |= RenderFlags.FerryConnections;
            if (CityNamesCheckBox.Checked) renderFlags |= RenderFlags.CityNames;
            return renderFlags;
        }

        private ExportFlags GetExportFlags()
        {
            ExportFlags exportFlags = 0;

            if (triStateTreeView1.GetCheckedByNodeName("GenTileMapInfo")) exportFlags |= ExportFlags.TileMapInfo;
            if (triStateTreeView1.GetCheckedByNodeName("GenCityList")) exportFlags |= ExportFlags.CityList;
            if (triStateTreeView1.GetCheckedByNodeName("GenCityDimensions")) exportFlags |= ExportFlags.CityDimensions; // TODO: add
            if (triStateTreeView1.GetCheckedByNodeName("GenCityLocalizedNames")) exportFlags |= ExportFlags.CityLocalizedNames;
            if (triStateTreeView1.GetCheckedByNodeName("GenCountryList")) exportFlags |= ExportFlags.CountryList;
            if (triStateTreeView1.GetCheckedByNodeName("GenCountryLocalizedNames")) exportFlags |= ExportFlags.CountryLocalizedNames;
            if (triStateTreeView1.GetCheckedByNodeName("GenOverlayList")) exportFlags |= ExportFlags.OverlayList;
            if (triStateTreeView1.GetCheckedByNodeName("GenOverlayPNGs")) exportFlags |= ExportFlags.OverlayPNGs;

            return exportFlags;
        }

        private void SetExportFlags()
        {
            triStateTreeView1.GetNodeByName("GenTileMapInfo").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.TileMapInfo);
            triStateTreeView1.GetNodeByName("GenCityList").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.CityList);
            //triStateTreeView1.GetNodeByName("GenCityDimensions").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.CityDimensions);
            triStateTreeView1.GetNodeByName("GenCityLocalizedNames").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.CityLocalizedNames);
            triStateTreeView1.GetNodeByName("GenCountryList").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.CountryList);
            triStateTreeView1.GetNodeByName("GenCountryLocalizedNames").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.CountryLocalizedNames);
            triStateTreeView1.GetNodeByName("GenOverlayList").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.OverlayList);
            triStateTreeView1.GetNodeByName("GenOverlayPNGs").Checked = SettingsManager.Current.Settings.TileGenerator.ExportFlags.IsActive(ExportFlags.OverlayPNGs);
        }

        private void GenerateBtn_Click(object sender, EventArgs e)
        {
            this.SaveSettings();

            /*var res = folderBrowserDialog1.ShowDialog();
            if (res == DialogResult.OK)
            {
                if (!Directory.Exists(folderBrowserDialog1.SelectedPath)) return;

                SettingsManager.Current.Settings.TileGenerator.LastTileMapPath = folderBrowserDialog1.SelectedPath;

                SettingsManager.Current.SaveSettings();

                GenerateTileMap();
            }*/

            GenerateTileMap();
        }
        
        private void GenTilesCheck_CheckedChanged(object sender, EventArgs e)
        {
            StartZoomLevelBox.Enabled = GenTilesCheck.Checked;
            EndZoomLevelBox.Enabled = GenTilesCheck.Checked;
            triStateTreeView1.GetNodeByName("GenTileMapInfo").Checked = GenTilesCheck.Checked;
        }

        private void SaveSettings_Click(object sender, EventArgs e)
        {
            this.SaveSettings();
        }

        private void SaveSettings()
        {
            var startZoomLevel = Convert.ToInt32(Math.Round(StartZoomLevelBox.Value, 0));
            var endZoomLevel = Convert.ToInt32(Math.Round(EndZoomLevelBox.Value, 0));

            if (startZoomLevel < 0 || endZoomLevel < 0)
            {
                MessageBox.Show("Cannot set start or end zoom level less than zero");
                return;
            }

            if (startZoomLevel > endZoomLevel)
            {
                MessageBox.Show("Cannot set start zoom level lower than end zoom level");
                return;
            }

            if (Convert.ToInt32(txtMapPadding.Text) <= 0)
            {
                MessageBox.Show("Map padding is invalid. Must be greater than 0");
                return;
            }

            if (Convert.ToInt32(txtTileSize.Text) <= 0)
            {
                MessageBox.Show("Tile size is invalid. Must be greater than 0");
            }


            SettingsManager.Current.Settings.TileGenerator.ExportFlags = GetExportFlags();
            SettingsManager.Current.Settings.TileGenerator.MapPadding = Convert.ToInt32(txtMapPadding.Text);
            SettingsManager.Current.Settings.TileGenerator.TileSize = Convert.ToInt32(txtTileSize.Text);
            SettingsManager.Current.Settings.TileGenerator.RenderFlags = GetRenderFlags();
            SettingsManager.Current.Settings.TileGenerator.GenerateTiles = GenTilesCheck.Checked;
            SettingsManager.Current.Settings.TileGenerator.StartZoomLevel = startZoomLevel;
            SettingsManager.Current.Settings.TileGenerator.EndZoomLevel = endZoomLevel;

            SettingsManager.Current.SaveSettings();
        }
    }
}
