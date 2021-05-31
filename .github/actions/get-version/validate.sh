#!/bin/bash

[[ $(grep -oPm1 '(?<=<AssemblyVersion>)[^<]+' "$1") =~ ^$2. ]] ||  exit 1
