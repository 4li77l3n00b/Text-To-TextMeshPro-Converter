# Legacy Text to TextMeshPro Converter

A tool to convert Legacy UI components (Text, InputField, Dropdown) to TextMeshPro components while preserving script references.

## Features
- Automatically reconnects references in your inspector after conversion.
- Regex-based upgrading of C# scripts from `Text` to `TMP_Text`, etc.
- Handles Text, InputField, and Dropdown.

## How to Use

1. **Backup**: Always backup your project before running automated tools.
2. **Open Tool**: Go to menu `Tools -> TMP Converter -> Open Converter Window`.
3. **Select Root**: Select your Canvas or UI root GameObject in the Hierarchy.
4. **Step 0 (Backup)**: Click "Scan & Backup References".
5. **Step 1 (Upgrade Code)**: Click "Upgrade C# Scripts". Wait for compilation.
   * *Note: If compilation errors occur due to local variables, please fix them manually (e.g., change `Text` to `var` or `TMP_Text`).*
   * *After fixing errors, ensure Unity compiles successfully.*
6. **Step 2 (Convert UI)**: Assign a Font Asset, then click "Convert UI Components".
7. **Step 3 (Restore)**: Click "Restore References" to fix the broken links in the Inspector.
