cypnode smoke test: process
===========================

This test suite tests the cypnode process itself, e.g.:

- can the process be executed and terminated?
- does cypnode correctly handle different configuration settings?
- do the command-line parameters work correctly?

These tests rely on a self-contained cypnode build. The executable location can be set using the ``cypnode`` variable.

Execute these tests as follows:

* ``robot process.rst``
* ``robot --variable cypnode:/home/johndoe/tangram/cypnode process.rst``

Test suite configuration
------------------------
.. code:: robotframework

  *** Settings ***
  Library   OperatingSystem
  Library   Process
  Library   String
  
  Resource  ../resources/variables.resource

  *** Variables ***

  *** Test Cases ***
  Start and stop cypnode
    [Documentation]  cypnode starts, is interrupted and logs no fatal errors
    Copy File   ${appsettings.default.json}  appsettings.json
    ${cypnode_handle} =  Start Process  ${cypnode_path}/cypnode  stdout=stdout.txt  stderr=stderr.txt
    Sleep  10s
    Process Should Be Running  ${cypnode_handle}
    Send Signal To Process  SIGINT  ${cypnode_handle}
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Contain  ${cypnode_result.stdout}  [FTL]

  Start cypnode without appsettings.json
    [Documentation]  cypnode without appsettings.json terminates itself with error code
    Remove File  appsettings.json
    ${cypnode_handle} =  Start Process  ${cypnode_path}/cypnode  stdout=stdout.txt  stderr=stderr.txt
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Be Equal As Integers  ${cypnode_result.rc}  0
