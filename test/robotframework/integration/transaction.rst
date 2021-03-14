cypnode integration test: transaction
=====================================

This test suite tests the transaction features

These tests rely on a self-contained cypnode build. The executable location can be set using the ``cypnode_path`` variable.

Execute these tests as follows:

* ``robot transaction.rst``
* ``robot --variable cypnode_path:/home/johndoe/tangram/cypnode transaction.rst``

Test suite configuration
------------------------
.. code:: robotframework

  *** Settings ***
  Library   Collections
  Library   OperatingSystem
  Library   Process
  Library   RequestsLibrary
  
  Resource  ../resources/common.resource

  
  *** Variables ***
  ${transaction_endpoint}=  /pool/transaction
  &{protobuf_headers}=      Content-Type=application/x-protobuf  Accept=application/json


  *** Keywords ***
  Transaction returns
    [Arguments]  ${session}  ${transaction}  ${http_code}  ${error_code}
    ${response}=          POST On Session  ${session}  ${transaction_endpoint}  data=${transaction}  headers=&{protobuf_headers}
    Status Should Be      ${http_code}     ${response}
    Dictionary Should Contain Item         ${response.json()}  code  ${error_code}


  *** Test Cases ***
  Send transaction
    [Documentation]  send transactions within block
    ${cypnode_handle}=    Start node       ${appsettings.default.json}

    ${transaction_00}=    Get Binary File  ./integration/resources/transaction_00.raw
    ${transaction_01}=    Get Binary File  ./integration/resources/transaction_01.raw

    Create Session        nodeSession      ${api_baseurl}
    
    Transaction returns   nodeSession      ${transaction_00}  200  200
    Transaction returns   nodeSession      ${transaction_01}  200  200

    Transaction returns   nodeSession      ${transaction_00}  200  500
    Transaction returns   nodeSession      ${transaction_01}  200  500

    Transaction returns   nodeSession      ${transaction_00}  200  500
    Transaction returns   nodeSession      ${transaction_01}  200  500

    Stop node             ${cypnode_handle}
