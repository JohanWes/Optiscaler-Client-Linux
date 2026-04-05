# Optiscaler Client Linux

WIP Linux fork of Optiscaler Client. I have only tested this on Arch-based systems so far, but it should behave similarly on other Linux distros.

Games are automatically scanned from Steam and Heroic, then OptiScaler can be installed into the detected Windows game directory.

## Install

```bash
curl -fsSL https://raw.githubusercontent.com/JohanWes/Optiscaler-Client-Linux/main/install.sh | bash
```

## Build

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```
