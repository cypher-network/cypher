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
  Library   String
  
  Resource  ../resources/variables.resource

  *** Variables ***

  *** Test Cases ***
  # cypnode starts, is interrupted, generates no fatal errors and returns with code 0
  Start and stop cypnode
    Copy File   smoke/resources/process_start_cypnode.appsettings.json  appsettings.json
    ${cypnode_handle} =  Start Process  ${cypnode}  stdout=stdout.txt  stderr=stderr.txt
    Sleep  10s
    Process Should Be Running  ${cypnode_handle}
    Send Signal To Process  SIGINT  ${cypnode_handle}
    Sleep  10s
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Be Equal As Integers  ${cypnode_result.rc}  0
    Should Not Contain  ${cypnode_result.stdout}  [FTL]

  # cypnode without appsettings.json terminates itself with error code
  Start cypnode without appsettings.json
    Remove File  appsettings.json
    ${cypnode_handle} =  Start Process  ${cypnode}  stdout=stdout.txt  stderr=stderr.txt
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Be Equal As Integers  ${cypnode_result.rc}  0
