cypnode smoke test: process
===========================

This test suite tests the cypnode process itself, e.g.:

- can the process be executed and terminated?
- does cypnode correctly handle different configuration settings?
- do the command-line parameters work correctly?

These tests rely on a self-contained cypnode build. The executable location can be set using the ``cypnode_path`` variable.

Execute these tests as follows:

* ``robot process.rst``
* ``robot --variable cypnode_path:/home/johndoe/tangram/cypnode process.rst``

Test suite configuration
------------------------
.. code:: robotframework

  *** Settings ***
  Library   OperatingSystem
  Library   Process
  Library   String
  
  Resource  ../resources/common.resource

  *** Variables ***

  *** Test Cases ***
  Start and stop cypnode
    [Documentation]  cypnode starts, is interrupted and logs no fatal errors
    ${cypnode_handle}=    Start node       ${appsettings.default.json}
    Stop node             ${cypnode_handle}

  Start cypnode without appsettings.json
    [Documentation]  cypnode without appsettings.json terminates itself with error code
    Remove File  appsettings.json
    ${cypnode_handle} =  Start Process  ${cypnode_path}/cypnode  stdout=stdout.txt  stderr=stderr.txt
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Be Equal As Integers  ${cypnode_result.rc}  0
