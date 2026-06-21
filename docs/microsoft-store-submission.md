# Microsoft Store MSIX

The `Build Microsoft Store MSIX` GitHub Actions workflow builds unsigned
x64 and ARM64 Store-ready MSIX packages and uploads them as a workflow
artifact.

## Repository variables

Configure these values under:

`Settings > Secrets and variables > Actions > Variables`

- `STORE_PACKAGE_IDENTITY_NAME`
- `STORE_PUBLISHER`
- `STORE_PUBLISHER_DISPLAY_NAME`

Copy all three values exactly from the app's Product identity page in Partner
Center. Values are case-sensitive. Do not add quotation marks around the
publisher value.

## Build in GitHub Actions

1. Open `Actions`.
2. Select `Build Microsoft Store MSIX`.
3. Select `Run workflow`.
4. Enter a version in `A.B.C.0` format, such as `1.0.0.0`.
5. Download the `PulseRelay-Store-A.B.C.0` artifact after the job succeeds.

The artifact contains:

- `PulseRelay_A.B.C.0_x64.msix`
- `PulseRelay_A.B.C.0_arm64.msix`
- `SHA256SUMS.txt`

Upload both MSIX packages to the Packages section of the same app submission
in Partner Center. These packages are intentionally unsigned; Microsoft signs
Store packages after certification and delivers the appropriate architecture
to each device.

## Local Windows build

Create `.local/store-identity.json` from
`packaging/msix/store-identity.example.json`, then run:

```powershell
pwsh .\scripts\build-store-msix.ps1 `
  -IdentityFile .\.local\store-identity.json `
  -Version 1.0.0.0 `
  -Runtime win-x64

pwsh .\scripts\build-store-msix.ps1 `
  -IdentityFile .\.local\store-identity.json `
  -Version 1.0.0.0 `
  -Runtime win-arm64
```

The Windows SDK must be installed because the script uses `MakeAppx.exe`.

The source logo remains `src/PulseRelay.Desktop/Assets/PulseRelay.png`.
Required package logo sizes are derived from that file during the build and are
written only under `artifacts/store/staging/Assets`.
