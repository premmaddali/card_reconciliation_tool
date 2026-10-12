# ReconTool session instructions

## Local application lifecycle

Use the managed PowerShell scripts in `C:\Projects\ReconTool\Scripts` for the local web application lifecycle. Do not start `dotnet run` or stop the process directly unless diagnosing a script failure.

- Start: `powershell -ExecutionPolicy Bypass -File C:\Projects\ReconTool\Scripts\Start-ReconTool.ps1`
- Stop: `powershell -ExecutionPolicy Bypass -File C:\Projects\ReconTool\Scripts\Stop-ReconTool.ps1`

The scripts manage port `5104`, the runtime PID file, and logs under `.recontool-runtime`.

After Razor or Tailwind class changes, run `npm run build:css` from `Recon.Web` before restarting through the start script.
