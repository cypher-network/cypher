# Contributing

Thank you for considering to help out with the source code! We welcome 
contributions from anyone on the internet, and are grateful for even the 
smallest of fixes!

If you'd like to contribute to cypher, please fork, fix, commit and send a 
pull request for the maintainers to review and merge into the main code base. If
you wish to submit more complex changes though, please check up with the core 
devs first on [our Discord channel](https://discord.gg/yVCSW5y2) to 
ensure those changes are in line with the general philosophy of the project 
and/or get some early feedback which can make both your efforts much lighter as
well as our review and merge procedures quick and simple.

## Coding guidelines

Please make sure your contributions adhere to our coding guidelines as
described in https://github.com/cypher-network/cypher/wiki/Development:

* All code is written using the coding guidelines of the .NET runtime:
  https://github.com/dotnet/runtime/blob/master/docs/coding-guidelines/coding-style.md,
  unless stated otherwise in this document.

* The following deviations from the .NET runtime coding guidelines shall be used:
  * (none)

* The following additions to the .NET runtime coding guidelines apply:
  * (none)

 * Code must adhere to the official C# 
[formatting](...) guidelines 

 * Code must be documented adhering to the official C# 
[commentary](...) guidelines.

 * Commit messages must follow the Conventional Commits specification v1.0.0 and be
   lower-case unless it contains a word commonly only written with upper-case.
   * E.g. `fix(serf): handle alive nodes only`

 * Pull requests need to be based on and opened against the `master` branch.

 * Pull request titles must follow the following scheme: `<type_scope-commit_message`>,
   where the commit message has spaces replaced by underscores.
   * E.g. `fix_serf-handle_alive_nodes`

## Configuration, dependencies, and tests

Please see the [Developers' Guide](SOON)
for more details on configuring your environment, managing project dependencies
and testing procedures.
