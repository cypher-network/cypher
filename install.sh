#!/usr/bin/env bash
echo Beginning cypher node install
tmp_dir=$(mktemp -d -t ci-XXXXXXXXXX)
echo "Temporary directory ${tmp_dir} created"
echo Fetching latest release
version=$(curl https://api.github.com/repos/cypher-network/cypher/releases/latest | grep -Eo "\"tag_name\":\s*\"(.*)\"" | cut -d'"' -f4)
echo "Downloading cypher node $version..."
curl -L https://github.com/cypher-network/cypher/releases/download/$version/cypher.$version.zip > $tmp_dir/cypher.zip

echo "Extracting $tmp_dir/cypher.zip"

unzip -o $tmp_dir/cypher.zip -d $tmp_dir/cypher

if [ ! -d $HOME/.cypher ]
then
    echo "Creating $HOME/.cypher"
    mkdir $HOME/.cypher
fi

echo Copying distribution...

cp -fRv $tmp_dir/cypher/. $HOME/.cypher/dist

if [ ! -d $HOME/.cypher/bin ]
then
   echo "Creating $HOME/.cypher/bin"
   mkdir $HOME/.cypher/bin
fi

echo Copying cypnode command

cp -fRv $HOME/.cypher/dist/Runners/cypnode.sh $HOME/.cypher/bin/cypnode

echo Setting execute permission

chmod +x $HOME/.cypher/bin/cypnode

echo "Cleaning up temporary directory ${tmp_dir}"

rm -rf $tmp_dir

if grep -q "$HOME/.cypher/bin" ~/.profile
then
        :
else
        echo Reloading profile
        echo "" >> ~/.profile
        echo "export PATH=$PATH:$HOME/.cypher/bin" >> ~/.profile
fi

echo cypher node was installed successfully!
