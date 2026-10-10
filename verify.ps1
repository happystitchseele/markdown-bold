$ErrorActionPreference = 'Stop'
$folder = $PSScriptRoot.Replace('/', '\')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
$source = [IO.File]::ReadAllText("$folder\MarkdownBold.cs")
# Isolate tests: never install the system hook or send keys to the user's editor.
$source = $source.Replace('hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);', 'hook = (IntPtr)1;')
# Physical Ctrl presses in another application must not distort the isolated timing check.
$source = $source.Replace('static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }', 'static bool Down(int key) { return key == 0x11 && Verify.CtrlHeld; }')
$source = $source.Replace('[DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);', 'static uint SendInput(uint count, INPUT[] inputs, int size) { return Verify.RecordInputs(count, inputs); }')
try {
    [IO.File]::WriteAllText("$folder\VerifySource.cs", $source)
    & $compiler /nologo /target:exe /main:Verify "/out:$folder\Verify.exe" @references "$folder\VerifySource.cs" "$folder\Verify.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Verification build failed' }
    & "$folder\Verify.exe" $folder | Tee-Object -FilePath "$folder\verification.txt"
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
} finally {
    Remove-Item -LiteralPath "$folder\VerifySource.cs", "$folder\Verify.exe" -ErrorAction SilentlyContinue
}
