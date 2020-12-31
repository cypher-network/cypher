#!/usr/bin/env bash
tmp_dir=$(mktemp -d -t ci-XXXXXXXXXX)
echo $tmp_dir
version=$(curl https://api.github.com/repos/inkadnb/cypher/releases/latest | grep -Eo "\"tag_name\":\s*\"(.*)\"" | cut -d'"' -f4)

echo "Installing cypher node $version..."
curl -L https://github.com/inkadnb/cypher/releases/download/$version/cypher.$version.zip > $tmp_dir/cypher.zip
unzip -o $tmp_dir/cypher.zip -d $tmp_dir/cypher
mkdir $HOME/.cypher
cp -rf $tmp_dir/cypher $HOME/.cypher/dist
mkdir $HOME/.cypher/bin

cp -f $HOME/.cypher/dist/Runners/cypnode.sh $HOME/.cypher/bin/cypnode

chmod +x $HOME/.cypher/bin/cypnode
rm -rf $tmp_dir

if grep -q "$HOME/.cypher/bin" ~/.profile
then
        :
else
        echo "" >> ~/.profile
        echo "export PATH=$PATH:$HOME/.cypher/bin" >> ~/.profile
fi

echo cypher node was installed successfully!