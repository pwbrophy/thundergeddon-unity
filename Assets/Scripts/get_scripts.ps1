# Store the folder where this script is being run (the root output folder)
$root = Get-Location

# Get a list of all subfolders under the current folder, searching recursively
$folders = Get-ChildItem -Directory -Recurse

# Loop through each subfolder one at a time
foreach ($folder in $folders) {

    # Find all .cs files directly inside this specific subfolder (not deeper)
    $csFiles = Get-ChildItem -Path $folder.FullName -Filter *.cs -File

    # Only continue if this folder actually contains any .cs files
    if ($csFiles.Count -gt 0) {

        # Build the name of the output text file, based on the folder name (e.g. "GamePanel.txt")
        $outputFileName = $folder.Name + ".txt"

        # Build the full path to the output file in the root folder
        $outputPath = Join-Path $root.Path $outputFileName

        # Take all .cs files in this folder, sort them by full path, read their contents
        $csFiles | Sort-Object FullName | Get-Content |

        # Write the combined contents into the output text file in the root folder (overwrite if it exists)
        Set-Content $outputPath
    }
}
