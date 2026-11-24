# Third-party notices

## Community terminology

`src/glossary.json`: Star Citizen Chinese Wiki localization group and all contributors to [ParaTranz project 8340](https://paratranz.cn/projects/8340/terms), snapshot 2026-11-24. Filtered, deduplicated, and supplemented. **CC BY-NC-SA 4.0**, including this adapted data. Full attribution and change description: [GLOSSARY-NOTICE.txt](src/GLOSSARY-NOTICE.txt). This data is not under the code's MIT license.

## Bundled Chinese ↔ English translation models

Argos package `translate-en_zh-1_9`, SHA-256 `433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab`.

Reverse package `translate-zh_en-1_9`, SHA-256 `62e7af5a3a48b530e47b7b3e5c78c2de79073ecd815750d2bf3ab35b4a67da2d`.

Both model packages' original READMEs state **CC BY 4.0** for the underlying OPUS-MT model. Authors: **Jörg Tiedemann and Santhosh Thottingal**, “OPUS-MT — Building open translation services for the World”, Proceedings of the 22nd Annual Conference of the European Association for Machine Translation, 2020, Lisbon, Portugal.

Source: [Argos package index](https://github.com/argosopentech/argospm-index), [OPUS-MT](https://github.com/Helsinki-NLP/Opus-MT). Packaged model used without changing its weights; this application performs inference with CTranslate2. The original README is included in `models/translate-en_zh-1_9/README.md` in the installed payload. [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). No endorsement is implied.

## Bundled runtime

The EXE embeds an extractable ZIP. All upstream license/notice files remain inside the embedded Python runtime and each package's `*.dist-info/licenses` directory, including licenses of vendored components:

- Python 3.12.10: PSF license and bundled third-party notices (`runtime/LICENSE.txt`).
- CTranslate2: MIT; upstream binary dependency notices preserved.
- SentencePiece and gRPC: Apache 2.0.
- NumPy: BSD-3-Clause and bundled library notices (including OpenBLAS where present).
- PyYAML: MIT. Setuptools: MIT and vendored component notices. typing_extensions: PSF-2.0.
- Certifi/Mozilla CA bundle: MPL 2.0; source bundle retained unmodified with its package metadata.

Exact versions, download URLs and SHA-256 checksums: `dependencies.json`. Their licenses apply independently from this project's MIT license.

## Optional local Hy engine

Not embedded; fetched only when selected:

- [Tencent Hunyuan Hy-MT3](https://github.com/Tencent-Hunyuan/Hy-MT3), model `Hy-MT3-1.8B-Q4_K_M.gguf`: Apache 2.0. Pinned revision and digest in `src/local_hy.py`.
- [llama.cpp](https://github.com/ggml-org/llama.cpp), Windows CPU runtime b12676: MIT; archive's own third-party notices are retained.

This project includes no Star Citizen game assets, localization files, login credentials or proprietary executable code. Trademark ownership remains with respective holders.
