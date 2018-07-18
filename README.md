# GoogleDocs Data Downloader
Downloader of google docs sheets in csv format with optional converting it to json.

## How to use
* Open `Window` / `Leopotam` / `GoogleDocs Downloader` window.
* Add required urls (direct link from `SHARE` wizard of GoogleDocs) and paths to files at current project. Paths should be relative to `Assets` folder.
* Press `Download data` button.
* Enjoy.

## GoogleDocs sheet headers for JSON translation
* First line - always will be recognized as JSON field names.
* Second line - JSON data wrappers. For example, strings should be wrapped with "", arrays should be wrapped with [] and so on.
    > Any column with `IGNORE` value in second line will be skipped. Can be used for comments.
* First column can be recognized as keys in JSON mode "To dictionary" (will be excluded from data).

> Tested on unity 2018.2 (dependent on it) and contains assembly definition for compiling to separate assembly file for performance reason.