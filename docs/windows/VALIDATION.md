# Validation recorded on 17 November 2026

| Check | Result |
| --- | --- |
| .NET SDK | 10.0.404, macOS ARM64 build host |
| Core build | Passed |
| Windows x64 self-contained publish | Passed |
| Windows ARM64 self-contained publish | Passed |
| Compiler diagnostics in final builds | No warnings or errors |
| Windows executable headers | x86-64 and AArch64 PE32+ GUI executables confirmed |
| Regression/integration checks | 33 passed |
| ExifTool used for executed integration checks | macOS ExifTool 13.50 |
| ExifTool bundled for Windows | Official Windows x64 distribution 13.64, including all support files/licenses |
| Windows native executable/runtime tests | Not run: no Windows host attached |
| Windows Mica/input/codec/map visual QA | Pending on Windows; see WINDOWS-QA.md |

The checks exercise real JPEG and MP4 metadata writes and copies, camera read-back verification, absent lens fields, literal argument handling, backup preservation, file modification dates, HDR/ambiguous/same-file matching, recursive drops, workflow JSON compatibility/persistence/deduplication/order and mixed/missing batch fields.

No personal media was used in the shipped test fixtures. No personal workflow library is included. The Windows smoke-test command and GitHub Actions workflow are provided for runtime validation, but their existence is not a claim that they were executed in this session.
