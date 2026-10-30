# spektra-cli plugin for Claude Code

Teaches Claude Code to use [`spektra-cli`](https://github.com/rarepops/Spektra/blob/main/docs/cli.md): which command answers which question about a music library (fake lossless files, corruption, duplicates, what one folder has that another lacks, loudness, tags), how to read its verdicts and exit codes, and never to change your files without asking. Install it from inside Claude Code:

    /plugin marketplace add rarepops/Spektra
    /plugin install spektra-cli@spektra

The plugin does not install the CLI itself: get it with `npm install -g spektra-cli` or from the [releases page](https://github.com/rarepops/Spektra/releases/latest), and put ffmpeg and ffprobe on your `PATH`. Part of Spektra, under the [PolyForm Perimeter License 1.0.1](https://github.com/rarepops/Spektra/blob/main/LICENSE.md).
