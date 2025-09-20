using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Updater_2
{
    public partial class UI : Form
    {
        public UI()
        {
            UI_Forms = this;
            InitializeComponent();
        }

        public class NameVersion
        {
            public string name;
            public string version;
        }

        public static UI UI_Forms;
        public bool menuEnable = true;
        string filePath = string.Empty;
        public static Hashtable Camera = new Hashtable();


        public static void UI_Lock()
        {
            UI_Forms.menuEnable = false;
            UI_Forms.StartIP.Enabled = false;
            UI_Forms.StopIP.Enabled = false;
            UI_Forms.sshLogin.Enabled = false;
            UI_Forms.sshPass.Enabled = false;
            UI_Forms.webPort.Enabled = false;
            UI_Forms.sshPort.Enabled = false;
            UI_Forms.checkSaveSettings.Enabled = false;
            UI_Forms.checkBoxFolder.Enabled = false;
            UI_Forms.maxParallelism.Enabled = false;
           // UI_Forms.dataGridView.Enabled = false;
        }

        public static void UI_UnLock()
        {
            UI_Forms.menuEnable = true;
            UI_Forms.StartIP.Enabled = true;
            UI_Forms.StopIP.Enabled = true;
            UI_Forms.sshLogin.Enabled = true;
            UI_Forms.sshPass.Enabled = true;
            UI_Forms.webPort.Enabled = true;
            UI_Forms.sshPort.Enabled = true;
            UI_Forms.checkSaveSettings.Enabled = true;
            UI_Forms.checkBoxFolder.Enabled = true;
            UI_Forms.maxParallelism.Enabled = true;
            //UI_Forms.dataGridView.Enabled = true;
        }

        void Drop_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        void Drop_DragDrop(object sender, DragEventArgs e)
        {
            if (!menuEnable)
            {
                MessageBox.Show("Update in progress.", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (string obj in (string[])e.Data.GetData(DataFormats.FileDrop))
            {
                if (!Directory.Exists(obj))
                {
                    if (obj.Substring(obj.LastIndexOf('.')) == ".xml")
                    {
                        UI_Lock();

                        progressBar.Value = 0;
                        dataGridView.Columns.Clear();
                        Camera.Clear();
                        SearchFactor.Drop(obj, webPort.Text);
                    }
                }
            }
        }

        public static void FactorsAdd(string ip, NameVersion obj)
        {
            Camera.Add(ip, obj);
            UI_Forms.progressBar.PerformStep();
        }

        public static void SetMaxProgressBar(int max)
        {
            UI_Forms.progressBar.Maximum = max;
        }

        public static void StepProgressBar()
        {
            UI_Forms.progressBar.PerformStep();
        }

        public static void FullProgressBar()
        {
            UI_Forms.progressBar.Value = UI_Forms.progressBar.Maximum;
        }

        public static void AddDataGridView()
        {
            DataGridViewCheckBoxColumn CheckboxColumn = new DataGridViewCheckBoxColumn();
            CheckboxColumn.Width = 25;
            CheckboxColumn.TrueValue = true;
            CheckboxColumn.FalseValue = false;
            UI_Forms.dataGridView.Columns.Add(CheckboxColumn);

            UI_Forms.dataGridView.Columns.Add("IP", "IP");
            UI_Forms.dataGridView.Columns[1].Width = 90;
            UI_Forms.dataGridView.Columns[1].ReadOnly = true;
            UI_Forms.dataGridView.Columns.Add("Name", "Name");
            UI_Forms.dataGridView.Columns[2].MinimumWidth = 100;
            UI_Forms.dataGridView.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            UI_Forms.dataGridView.Columns[2].ReadOnly = true;

            UI_Forms.dataGridView.Columns.Add("Version", "Version");
            UI_Forms.dataGridView.Columns[3].MinimumWidth = 60;
            UI_Forms.dataGridView.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            UI_Forms.dataGridView.Columns[3].ReadOnly = true;

            ICollection cameraKeys = UI.Camera.Keys;
            foreach (string ipCameraKey in cameraKeys)
            {
                NameVersion nameVersion = (NameVersion)Camera[ipCameraKey];

                if (UI_Forms.dataGridView.InvokeRequired)
                {
                    UI_Forms.dataGridView.Invoke((Action)(() => UI_Forms.dataGridView.Rows.Add(new object[]
                    {
                        (nameVersion.name.ToString() == "IP is unavailable" | nameVersion.name.ToString() == "Not a Factor") ? false : true,
                        ipCameraKey, nameVersion.name, nameVersion.version

                    })));
                }
                else
                {
                    UI_Forms.dataGridView.Rows.Add(new object[]
                    {
                        (nameVersion.name.ToString() == "IP is unavailable" | nameVersion.name.ToString() == "Not a Factor") ? false : true,
                        ipCameraKey, nameVersion.name, nameVersion.version
                    });
                }
            }
        }

        void AddFileDataGridView(string name)
        {
            if (dataGridView.Columns.Contains(name)) return;

            var newCol = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name,
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                ReadOnly = true
            };
            dataGridView.Columns.Add(newCol);
        }

        void checkSaveSettings_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkSaveSettings.Checked)
            {
                if (MessageBox.Show("Are you sure you want to disable saving settings during update.", "Saving settings when updating.", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    checkSaveSettings.Checked = false;
                }
                else
                {
                    checkSaveSettings.Checked = true;
                }
            }
        }

        void Search_Click(object sender, EventArgs e)
        {
            if (!menuEnable)
            {
                MessageBox.Show("Update in progress.", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            progressBar.Value = 0;
            dataGridView.Columns.Clear();

            bool startIp = SearchFactor.Check(StartIP.Text);
            bool stopIp = SearchFactor.Check(StopIP.Text);

            string send = "";
            if (!startIp)
            {
                send = $"Incorrect Start address: {StartIP.Text}";
            }
            if ((!startIp) & (!stopIp))
            {
                send += "\n";
            }
            if (!stopIp)
            {
                send += $"Incorrect Stop address: {StopIP.Text}";
            }
            if ((!startIp) | (!stopIp))
            {
                MessageBox.Show(send, "IP address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Camera.Clear();
            UI_Lock();

            SearchFactor.IpSearch(StartIP.Text, StopIP.Text, webPort.Text);
        }

        void Selects_Click(object sender, EventArgs e)
        {
            if (!menuEnable)
            {
                MessageBox.Show("Update in progress.", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (checkBoxFolder.Checked)
            {
                MessageBox.Show("Selected to load files from folder.", "Selected file to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Application.StartupPath.ToString();
                openFileDialog.Filter = "Files (*.tar.gz; *.deb; *.sh) | *.tar.gz; *.deb; *.sh";
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    fileBox.Text = openFileDialog.SafeFileName;
                    filePath = openFileDialog.FileName;
                }
            }
        }

        void Save_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.InitialDirectory = Application.StartupPath;
            saveFileDialog.Filter = "CSV|*.csv";
            saveFileDialog.FileName = "Updates result " + DateTime.Now.ToString("dd.MM.yyyy HH.mm");

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                FileInfo fil = new FileInfo(saveFileDialog.FileName);
                using (StreamWriter sw = fil.AppendText())
                {
                    var headers = dataGridView.Columns.Cast<DataGridViewColumn>();
                    sw.WriteLine(string.Join(";", headers.Select(column => "\"" + column.HeaderText + "\"").ToArray()));
                    sw.Close();
                }
                using (StreamWriter sw = fil.AppendText())
                {
                    foreach (DataGridViewRow row in dataGridView.Rows)
                    {
                        var cells = row.Cells.Cast<DataGridViewCell>();
                        sw.WriteLine(string.Join(";", cells.Select(cell => "\"" + cell.Value + "\"").ToArray()));
                    }
                    sw.Close();
                }
            }
        }

        async void Updates_Click(object sender, EventArgs e)
        {
            if (!menuEnable)
            {
                MessageBox.Show("Update in progress.", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UI_Lock();
            progressBar.Value = 0;
            if (dataGridView.RowCount != 0)
            {
                if (checkBoxFolder.Checked)
                {
                    string[] patterns = { "*.tar.gz", "*.deb", "*.sh" };
                    string[] files = patterns.SelectMany(pattern => Directory.GetFiles(Application.StartupPath, pattern, SearchOption.AllDirectories)).Distinct().OrderBy(file => Path.GetFileName(file)).ToArray();

                    if (files.Count() != 0)
                    {
                        foreach (string file in files)
                        {
                            AddFileDataGridView(file.Substring(file.LastIndexOf('\\') + 1));
                        }

                        progressBar.Maximum = dataGridView.Rows.Count * files.Count();

                        var resource = new SemaphoreSlim(maxParallelism.Value, maxParallelism.Value);

                        var tasks = Enumerable.Range(0, dataGridView.Rows.Count).Select(async row =>
                        {
                            await resource.WaitAsync();
                            try
                            {
                                //await UpdateFactor.MultipleFiles((bool)dataGridView.Rows[row].Cells[0].Value, dataGridView.Rows[row].Cells["IP"].Value.ToString(), files, row);
                            }
                            finally
                            {
                                resource.Release();
                            }
                        });

                        await Task.WhenAll(tasks);

                    }
                    else
                    {
                        MessageBox.Show("No file to update", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UI_UnLock();
                        return;
                    }
                }
                else
                {
                    if (filePath != "")
                    {
                        AddFileDataGridView(fileBox.Text);
                        progressBar.Maximum = dataGridView.Rows.Count;

                        var resource = new SemaphoreSlim(maxParallelism.Value, maxParallelism.Value);

                        var tasks = Enumerable.Range(0, dataGridView.Rows.Count).Select(async row =>
                        {
                            await resource.WaitAsync();
                            try
                            {
                                //await UpdateFactor.SingleFile((bool)dataGridView.Rows[row].Cells[0].Value, dataGridView.Rows[row].Cells["IP"].Value.ToString(), filePath, row);
                            }
                            finally
                            {
                                resource.Release();
                            }
                        });

                        await Task.WhenAll(tasks);

                    }
                    else
                    {
                        MessageBox.Show("No file to update", "File to update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UI_UnLock();
                        return;
                    }
                }
            }
            else
            {
                MessageBox.Show("There are no complexes to update.", "Complexes to update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UI_UnLock();
                return;
            }

            progressBar.Value = progressBar.Maximum;

            if (MessageBox.Show("Save the list of updated complexes.", "Complexes to update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string fileName = "Updates result " + DateTime.Now.ToString("dd.MM.yyyy HH.mm");
                FileInfo fil = new FileInfo(Application.StartupPath + "\\" + fileName + ".csv");
                using (StreamWriter sw = fil.AppendText())
                {
                    var headers = dataGridView.Columns.Cast<DataGridViewColumn>();
                    sw.WriteLine(string.Join(";", headers.Select(column => "\"" + column.HeaderText + "\"").ToArray()));
                    sw.Close();
                }
                using (StreamWriter sw = fil.AppendText())
                {
                    foreach (DataGridViewRow row in dataGridView.Rows)
                    {
                        var cells = row.Cells.Cast<DataGridViewCell>();
                        sw.WriteLine(string.Join(";", cells.Select(cell => "\"" + cell.Value + "\"").ToArray()));
                    }
                    sw.Close();
                }
            }

            UI_UnLock();
        }

        void UI_MouseHover(object sender, EventArgs e)
        {
            toolTip.SetToolTip(StartIP, "Start address for search.");
            toolTip.SetToolTip(StopIP, "Final address to search for.");
            toolTip.SetToolTip(Search, "Starting a search.");
            toolTip.SetToolTip(sshLogin, "SSH access login.");
            toolTip.SetToolTip(sshPass, "SSH access password.");
            toolTip.SetToolTip(webPort, "Web port.");
            toolTip.SetToolTip(sshPort, "SSH port.");
            //toolTip.SetToolTip(fileBox, "xxx.");
            toolTip.SetToolTip(checkSaveSettings, "Preserve settings when updating.");
            //toolTip.SetToolTip(dataGridView, "xxx.");
            toolTip.SetToolTip(checkBoxFolder, "Update from the program folder or the selected file.");
            toolTip.SetToolTip(Selects, "Select a file to update.");
            toolTip.SetToolTip(Save, "Saving the update table.");
            toolTip.SetToolTip(Updates, "Starting the update.");
            toolTip.SetToolTip(labelNumber, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelUpdate, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_1, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_2, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_3, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_4, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_5, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_6, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_7, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_8, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_9, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(labelNumber_10, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(maxParallelism, "The number of parallel updates is from 1 to 10.");
            toolTip.SetToolTip(progressBar, "Progress bar for searching for complexes or performing updates.");
        }

    }
}
