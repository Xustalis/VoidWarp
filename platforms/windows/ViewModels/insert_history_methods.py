
import os

file_path = "G:\\project\\VoidWarp\\platforms\\windows\\ViewModels\\MainViewModel.cs"
with open(file_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

# 1. Add using System.Text.Json and System.Collections.Generic
insert_using_index = -1
for i, line in enumerate(lines):
    if "using System.Linq;" in line:
        insert_using_index = i + 1
        break

if insert_using_index != -1:
    if not any("using System.Text.Json;" in line for line in lines):
        lines.insert(insert_using_index, "using System.Text.Json;\n")
    if not any("using System.Collections.Generic;" in line for line in lines):
        lines.insert(insert_using_index, "using System.Collections.Generic;\n")

# 2. Add ReceivedFiles collection property
# Look for PendingFiles definition
insert_prop_index = -1
for i, line in enumerate(lines):
    if "public ObservableCollection<PendingFileInfo> PendingFiles" in line:
        insert_prop_index = i + 1
        break

if insert_prop_index != -1:
    lines.insert(insert_prop_index, "        public ObservableCollection<ReceivedFileInfo> ReceivedFiles { get; } = new();\n")

# 3. Add DeleteReceivedFileCommand definition
# Look for AddManualPeerCommand
insert_cmd_def_index = -1
for i, line in enumerate(lines):
    if "public ICommand AddManualPeerCommand { get; }" in line:
        insert_cmd_def_index = i + 1
        break

if insert_cmd_def_index != -1:
    lines.insert(insert_cmd_def_index, "        public ICommand DeleteReceivedFileCommand { get; }\n")

# 4. Initialize Command
# Look for AddManualPeerCommand init
insert_cmd_init_index = -1
for i, line in enumerate(lines):
    if "AddManualPeerCommand = new RelayCommand" in line:
        insert_cmd_init_index = i + 1
        break

if insert_cmd_init_index != -1:
    lines.insert(insert_cmd_init_index, "            DeleteReceivedFileCommand = new RelayCommand(file => DeleteReceivedFile(file as ReceivedFileInfo));\n")

# 5. Initialize History Loading (LoadHistory call in constructor)
# Look for InitializeAsync call
insert_load_index = -1
for i, line in enumerate(lines):
    if "_ = InitializeAsync();" in line:
        insert_load_index = i + 1
        break

if insert_load_index != -1:
    lines.insert(insert_load_index, "                _ = LoadHistoryAsync();\n")

# 6. Add Methods (LoadHistoryAsync, SaveHistory, DeleteReceivedFile) used by history
# Insert before #region Helpers
insert_methods_index = -1
for i, line in enumerate(lines):
    if "#region Helpers" in line:
        insert_methods_index = i
        break

methods_code = """
        private async Task LoadHistoryAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidWarp", "history.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var history = JsonSerializer.Deserialize<List<ReceivedFileInfo>>(json);
                        if (history != null)
                        {
                            InvokeOnUI(() =>
                            {
                                foreach (var item in history)
                                {
                                    item.FileExists = File.Exists(item.FilePath); // Update existence status
                                    ReceivedFiles.Add(item);
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"Failed to load history: {ex.Message}");
                }
            });
        }

        private void SaveHistory()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidWarp");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                
                var filePath = Path.Combine(path, "history.json");
                var json = JsonSerializer.Serialize(ReceivedFiles.ToList());
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                AddLog($"Failed to save history: {ex.Message}");
            }
        }

        private void DeleteReceivedFile(ReceivedFileInfo? file)
        {
            if (file == null) return;

            var dialog = new DeleteConfirmationDialog();
            dialog.Owner = Application.Current.MainWindow;
            
            if (dialog.ShowDialog() == true)
            {
                // Remove from list
                ReceivedFiles.Remove(file);
                SaveHistory();

                // Delete physical file if requested
                if (dialog.ShouldDeleteFile && File.Exists(file.FilePath))
                {
                    try
                    {
                        File.Delete(file.FilePath);
                        AddLog($"Deleted file: {file.FileName}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
"""

if insert_methods_index != -1:
    lines.insert(insert_methods_index, methods_code)


with open(file_path, 'w', encoding='utf-8') as f:
    f.writelines(lines)

print("MainViewModel updated successfully.")
