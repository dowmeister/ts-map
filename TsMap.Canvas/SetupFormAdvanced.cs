using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TsMap.Canvas
{
    public partial class SetupFormAdvanced : Form
    {
        private TsMapSet currentMapSet = null;
        private readonly BindingList<Mod> availableMods = new BindingList<Mod>();
        private readonly BindingList<Mod> selectedMods = new BindingList<Mod>();
        private readonly BindingList<TsMapSet> mapSets = new BindingList<TsMapSet>();

        public SetupFormAdvanced()
        {
            InitializeComponent();

            mapSets.AddRange<TsMapSet>(JsonHelper.LoadMapSets());

            cmbMapSets.DataSource = mapSets;
            cmbMapSets.DisplayMember = "Name";

            currentMapSet = new TsMapSet();
            lstMods.DataSource = availableMods;
            lstSelectedMods.DataSource = selectedMods;

            if (SettingsManager.Current.Settings.LastGamePath != null)
            {
                fldBrwDlgGamePath.SelectedPath = SettingsManager.Current.Settings.LastGamePath;
            }

            if (SettingsManager.Current.Settings.LastModPath != null)
            {
                folderBrowserDialogModFolder.SelectedPath = SettingsManager.Current.Settings.LastModPath;
            }

            if (SettingsManager.Current.Settings.LastTileMapPath != null)
            {
                folderBrowserDialogOutputPath.SelectedPath = SettingsManager.Current.Settings.LastTileMapPath;
            }

            if (!String.IsNullOrEmpty(RunOptionsContext.Current.Options.MapSet))
            {
                cmbMapSets.SelectedItem = mapSets.FirstOrDefault(m => m.Name == RunOptionsContext.Current.Options.MapSet);

                if (RunOptionsContext.Current.Options.GenerateTiles || RunOptionsContext.Current.Options.ExportInfo)
                {
                    this.ShowCanvas();
                }
            }
        }

        private void BtnBrowseFolder_Click(object sender, EventArgs e)
        {
            var result = fldBrwDlgGamePath.ShowDialog();

            if (result == DialogResult.OK)
            {
                txtPath.Text = SettingsManager.Current.Settings.LastGamePath = fldBrwDlgGamePath.SelectedPath;
                SettingsManager.Current.SaveSettings();
            }
        }

        private void ChkLoadMods_CheckedChanged(object sender, EventArgs e)
        {
            if (chkLoadMods.Checked)
            {
                grpMods.Visible = true;
            }
            else
            {
                grpMods.Visible = false;
            }
        }

        private void BtnSelectModsFolder_Click(object sender, EventArgs e)
        {
            var result = folderBrowserDialogModFolder.ShowDialog();

            if (result == DialogResult.OK)
            {
                SettingsManager.Current.Settings.LastModPath = folderBrowserDialogModFolder.SelectedPath;
                SettingsManager.Current.SaveSettings();

                if (currentMapSet == null)
                    currentMapSet = new TsMapSet();

                currentMapSet.ModFolderPath = folderBrowserDialogModFolder.SelectedPath;
                this.LoadMods();
            }
        }

        private void BtnSelectMod_Click(object sender, EventArgs e)
        {
            this.SelectMod();
        }

        private void BtnRemoveMod_Click(object sender, EventArgs e)
        {
            this.RemoveMod();
        }

        private void BtnIncreasePriority_Click(object sender, EventArgs e)
        {
            ChangePriority(selectedMods, lstSelectedMods.SelectedIndex, -1);
        }

        private void BtnDecreasePriority_Click(object sender, EventArgs e)
        {
            ChangePriority(selectedMods, lstSelectedMods.SelectedIndex, +1);
        }

        private void LoadMods()
        {
            selectedMods.Clear();
            availableMods.Clear();

            var files = Directory.EnumerateFiles(currentMapSet.ModFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            List<Mod> mods = files.Select(x => new Mod(x)).ToList();

            if (currentMapSet.Mods != null && currentMapSet.Mods.Count > 0)
            {
                foreach (Mod alreadySelectedMod in currentMapSet.Mods)
                {
                    Mod modToRemove = mods.FirstOrDefault(m => m.ModPath == alreadySelectedMod.ModPath);

                    if (modToRemove != null)
                        mods.Remove(modToRemove);
                }
            }

            availableMods.AddRange<Mod>(mods);
            selectedMods.AddRange<Mod>(currentMapSet.Mods);
        }

        private void LstMods_DoubleClick(object sender, EventArgs e)
        {
            this.SelectMod();
        }

        private void LstSelectedMods_DoubleClick(object sender, EventArgs e)
        {
            this.RemoveMod();
        }
        
        private void SelectMod()
        {
            if (lstMods.SelectedItem != null)
            {
                var mod = (Mod)lstMods.SelectedItem;
                selectedMods.Add(mod);
                availableMods.Remove(mod);
            }
        }

        private void RemoveMod()
        {
            if (lstSelectedMods.SelectedItem != null)
            {
                var mod = (Mod)lstSelectedMods.SelectedItem;
                selectedMods.Remove(mod);
                availableMods.Add(mod);
            }
        }

        private void ChangePriority(BindingList<Mod> list, int index, int direction)
        {
            int newIndex = index + direction;

            if (newIndex >= list.Count) return;

            if (index < 0 || newIndex < 0) return;

            Mod itemToMove = list[index];
            Mod itemToReplace = list[newIndex];

            list[index] = itemToReplace;
            list[newIndex] = itemToMove;

            lstSelectedMods.SelectedIndex = newIndex;
        }

        private void btnSaveMapSet_Click(object sender, EventArgs e)
        {
            this.SaveCurrentMapSet();            
        }

        private void SaveCurrentMapSet()
        {
            if (txtMapSetName.Text == string.Empty) return;
            if (txtPath.Text == string.Empty) return;

            if (currentMapSet == null)
                currentMapSet = new TsMapSet();

            currentMapSet.Name = txtMapSetName.Text;
            currentMapSet.Path = txtPath.Text;
            currentMapSet.LoadMods = chkLoadMods.Checked;
            currentMapSet.FilesFilter = txtFileFilters.Text;

            if (chkLoadMods.Checked)
            {
                currentMapSet.Mods = selectedMods.ToList();
            }

            int index = -1;

            for (int i = 0; i < mapSets.Count; i++)
            {
                if (mapSets[i].Name == currentMapSet.Name)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
            {
                mapSets.Add(currentMapSet);
            }
            else
            {
                mapSets[index] = currentMapSet;
            }

            JsonHelper.SaveMapSets(mapSets.ToList());
        }

        private void btnNewMapSet_Click(object sender, EventArgs e)
        {
            cmbMapSets.SelectedItem = null;

            txtMapSetName.Text = txtPath.Text = txtOutputPath.Text = string.Empty;
            chkLoadMods.Checked = false;
            availableMods.Clear();
            selectedMods.Clear();
        }

        private void btnContinue_Click(object sender, EventArgs e)
        {
            this.SaveCurrentMapSet();

            if (String.IsNullOrEmpty(currentMapSet.Path)) return;

            this.ShowCanvas();
            //Close();
        }

        private void ShowCanvas()
        {
            MapSetContext.Current.MapSet = currentMapSet;

            Cursor = Cursors.WaitCursor;

            new TsMapCanvas().Show();
        }

        private void SetupFormAdvanced_Load(object sender, EventArgs e)
        {

        }

        private void cmbMapSets_SelectedIndexChanged(object sender, EventArgs e)
        {
            availableMods.Clear();
            selectedMods.Clear();

            currentMapSet = (TsMapSet)cmbMapSets.SelectedItem;

            if (currentMapSet != null)
            {
                txtMapSetName.Text = currentMapSet.Name;
                txtPath.Text = currentMapSet.Path;
                chkLoadMods.Checked = currentMapSet.LoadMods;
                txtOutputPath.Text = currentMapSet.OutputPath;
                txtFileFilters.Text = currentMapSet.FilesFilter;

                if (currentMapSet.LoadMods && !String.IsNullOrEmpty(currentMapSet.ModFolderPath))
                {
                    this.LoadMods();
                }
            }
        }

        private void btnBrowseOutputPath_Click(object sender, EventArgs e)
        {
            var result = folderBrowserDialogOutputPath.ShowDialog();

            if (result == DialogResult.OK)
            {
                if (currentMapSet == null)
                    currentMapSet = new TsMapSet();

                txtOutputPath.Text = currentMapSet.OutputPath = SettingsManager.Current.Settings.LastTileMapPath = folderBrowserDialogOutputPath.SelectedPath;

                SettingsManager.Current.SaveSettings();
            }
        }
    }
}
