a stupidly complicated way of downloading files slightly quicker. designed to circumvent unbalanced chunk download speed.

very scientific comparison with 100mib file on [external server](https://proof.ovh.net/files/) with 20 connections (mean of 3 full downloads):
- jdownloader 2: 2m 2s (very fast at first, tapers off at the end)
- this program (`--self-contained -r win-x64 -c Release -p:PublishTrimmed=True`): 28s

professional 100% inductive proof:
![](img/science.png)
