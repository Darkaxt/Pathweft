# Distribution notices and corresponding source

Pathweft's combined QuickLook-derived application is GPL-3.0-or-later; see
COPYING-GPL-3.0.txt. Original File Labs code retains its MIT notice in LICENSE.
Original selected-source notices, hashes and revisions are under vendor/.
Those notices remain distributed with the application.

| Component | Version/source | Distribution material |
| --- | --- | --- |
| File Labs | imported a8d5a5c, selected fixes through 9bfbbce | Original MIT LICENSE; Pathweft source/tag and upstream history |
| EverythingToolbar | a4379f181ea8e9b4d3ff8b0e5178d8d39491830c | MIT, vendor/EverythingSDK/NOTICES.txt |
| Everything SDK / engine | API2 / 1.4.1.1032 x64 | Native SDK and unchanged engine licenses/notices, Everything/License.txt |
| Diskweft selected scanner source | 82ac41c9a74f3cd0ee6ae44c6bdeb0c7328eb980 | MIT, Protagonist Labs copyright and complete terms in vendor/Diskweft/NOTICES.txt; selected original file hashes and adapted Pathweft source in the application source archive. No standalone Diskweft app ships |
| QuickLook selected viewers | 2ec219aa18416c6e3b89d6aad3d82d10610f917f | Selected file GPL headers plus Common MIT notice; full adapted source and original references |
| 7-Zip | 26.05 | LGPL/BSD/unRAR terms under SevenZip/ and vendor/SevenZip; full matching source archive |
| SharpSevenZip | 2.0.130-pathweft.1, upstream 9b2379b | LGPL-3.0-or-later; full original source, reviewed empty-directory.patch and reproduction scripts |
| rars / Pathweft bridge | 0.12.3 / e2415a2 | Apache-2.0 donor, GPL-3.0-or-later bridge; Cargo.lock, native source and vendored locked dependencies |
| F3D and linked runtime dependencies | 4.0.0 / b72c7b5 | BSD-3-Clause core; all supplied dependency licenses under F3D/licenses; matching source/build recipes |
| FFmpeg / OpenH264 in F3D's runtime | 9.0.1 / 2.6.0 | Actual avcodec configuration reports LGPL-2.1-or-later and libopenh264; full source archives and matching superbuild configure/patch recipes |
| MPV / media FFmpeg / libplacebo | e471496c56 / 9a861e9687 / 285d1cc8af | Darkaxt/vlc redesign-v1.1.1 source baseline with Pathweft's isolated FriBidi refresh; GPL/version3 linked media build. MPV/licenses contains verified linked-library notices; vendor/MPV identifies every source/hash; corresponding dependency source includes the native build/patch/SDK recipes. Separate from F3D's FFmpeg |
| FriBidi in libmpv/libass | 1.0.18 | LGPL-2.1-or-later; exact source/license and isolated Pathweft refresh recipe accompany the native runtime. Other media source revisions retain the donor baseline |
| Marked in the editor | 18.1.2 / 5beecae42b | MIT, vendor/Marked/LICENSE.md; verified npm bundle plus complete pinned Git source in the dependency archive |
| Markdig | 1.5.0 | BSD-2-Clause, vendor/Markdig |
| System.IO.Hashing | 10.0.14 | MIT, Microsoft .NET runtime source/license |
| WebView2 SDK | 1.0.4314.35 | Microsoft SDK terms; installed Evergreen runtime is a separate Windows dependency |
| Monaco editor | Vendored snapshot, exact shipped hashes in release-manifest.json | Editor/vs and its supplied MIT license/notices |
| .NET Desktop runtime | 10.0.14 | Microsoft .NET MIT license/third-party notices; self-contained system runtime |
| Specialist managed parsers | Exact source/package/native assets in vendor/Specialists/source-manifest.json and vendor/Specialists/runtime-manifest.json | CsvHelper (Apache-2.0 choice), ELFSharp/LiteDB/MimeKit/BouncyCastle/Prefetch/ApkReader/managed MediaInfo/RtfPipe (MIT), OpenMcdf (MPL-2.0), Serilog/SQLitePCLRaw (Apache-2.0), Microsoft.Data.Sqlite (MIT). Original full license texts under vendor/Specialists/licenses; full source and original package archives accompany the dependency-source release |
| Native MediaInfo | Official signed26.10 DLL; vendor/Specialists/mediainfo-runtime.json | MediaInfoLib BSD-2-Clause, ZenLib/zlib permissive notices under licenses/MediaInfo. Official all-inclusive source and original Windows DLL package accompany the dependency-source release. No installer or Shell extension is used |
| Embedded Office cores | WordCraft0.15.1 / GridCraft0.16.0 / DeckCraft0.15.0; exact source/native/font pins and ABI under vendor/Office | Source-built DLLs/minimal Pathweft bridges in an owned worker; MIT/Apache-2.0, original font/OFL and Rust dependency notices under Office/licenses. Complete donor/font archives, selected core graphs and156 dependency archives accompany corresponding sources. No Craft CLI/GUI/MCP ships; candidate status blocks final packaging until acceptance passes |
| LibreOffice fallback | Official26.2.7 Windows x64 / source26.2.7.3; selected9124-member runtime, MSI/source hashes under vendor/Office | Original MPL-2.0/LGPLv3+ and third-party terms in Office/LibreOffice/LICENSE.html, license.txt and NOTICE; complete official core/dictionaries source packs contain external build/patch/download recipes. Dependency manifest preserves full source table/actual PE imports without claiming all platform/build inputs are linked. Private Python/UNO and original Microsoft app-local redistributable terms are supplied; no system installation/registration |

OpenMcdf is unmodified; its complete MPL-covered source is supplied. Recipients
may obtain and modify that source under MPL-2.0. RtfPipe's NuGet artifact omits an
exact repository revision; the recorded build-era source snapshot is explicitly
qualified as uncertain rather than an asserted byte-for-byte source identity.
The exact original NuGet artifact and MIT source/license are retained. ApkReader's
source archive omits a license file, but its shipped NuGet metadata explicitly
declares MIT and authors Soar360 QL-Win; both the declaration and standard terms
are preserved. No additional license is inferred for omitted donor services.

The matching normal release provides Pathweft-VERSION-source.zip and
Pathweft-VERSION-dependency-sources.zip alongside the binaries and checksums.
The dependency archive includes complete pinned 7-Zip, SharpSevenZip, F3D,
FFmpeg/OpenH264 sources, MPV's linked dependency/build-source closure and the native Rust dependency graph. Source acquisition and
build recipes are reviewable scripts in scripts/. The included F3D superbuild
revision 2aada7d4c6a1dff3f2288008031e8a31ffc0428f, from before the SDK release,
contains the matching FFmpeg shared-library/OpenH264 configure and Windows patch
recipes. The recipes inside F3D's own older workflow use a different codec build;
do not use those to describe the bundled runtime. Its actual native metadata is
checked directly during packaged verification. Keep OpenH264's supplied notices.
The interop source must receive vendor/SharpSevenZip/empty-directory.patch before
building 2.0.130-pathweft.1. Keep all original third-party notices.

For offline native Rust rebuilding, copy rust-vendor/ beside native/pathweft-rars,
put cargo-config.toml at that project's .cargo/config.toml, and change its directory
entry to that copied rust-vendor directory. Use the pinned toolchain and lockfile.
The bridge, application and replacement shared libraries are not signature-locked.

.NET runtime source/licenses: https://github.com/dotnet/runtime/tree/v10.0.14
Windows Desktop source/licenses: https://github.com/dotnet/wpf/tree/v10.0.14
WebView2 terms: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35
