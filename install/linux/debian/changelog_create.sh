#!/bin/bash

VERSION=`git describe --abbrev=0 --tags $(git rev-list --tags --skip=1 --max-count=1) | cut -c2-`
OUTPUT_FILE=$(dirname $(realpath $0))/changelog

cat <<EOF > ${OUTPUT_FILE}
tangram-cypnode (${VERSION}) unstable; urgency=medium

EOF

git describe --abbrev=0 --tags `git rev-list --tags --skip=1 --max-count=2` | sed '$!N;s/\n/.../' | git log --pretty=format:'  * %<(76,trunc)%s%n%n -- %an <%ae>  %aD%n%n' >> ${OUTPUT_FILE}
