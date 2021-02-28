cypnode smoke test: process
===========================

This test suite tests the cypnode process itself, e.g.:

- can the process be executed and terminated?
- does cypnode correctly handle different configuration settings?
- do the command-line parameters work correctly?

These tests rely on a self-contained cypnode build. The executable location can be set using the ``cypnode`` variable.

Execute these tests as follows:

* ``robot cypnode_process.rst``
* ``robot --variable /home/johndoe/tangram/cypnode cypnode_process.rst``

Test suite configuration
------------------------
.. code:: robotframework

  *** Settings ***
  Library   OperatingSystem
  Library   Process
  
  Resource  ../resources/variables.resource

  *** Variables ***

  *** Test Cases ***
  Start cypnode
    Copy File   smoke/resources/process_start_cypnode.appsettings.json  appsettings.json
    Start Process  ${cypnode}  stdout=stdout.txt  stderr=stderr.txt
