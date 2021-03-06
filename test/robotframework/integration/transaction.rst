cypnode integration test: transaction
=====================================

This test suite tests the transaction features

These tests rely on a self-contained cypnode build. The executable location can be set using the ``cypnode`` variable.

Execute these tests as follows:

* ``robot transaction.rst``
* ``robot --variable cypnode:/home/johndoe/tangram/cypnode transaction.rst``

Test suite configuration
------------------------
.. code:: robotframework

  *** Settings ***
  Library   Collections
  Library   OperatingSystem
  Library   Process
  Library   RequestsLibrary
  
  Resource  ../resources/variables.resource

  *** Variables ***
  ${transaction_endpoint}=  /pool/transaction
  &{protobuf_headers}=  Content-Type=application/x-protobuf  Accept=application/json
  
  *** Test Cases ***
  Send transaction
    [Documentation]  send transactions within block
    Copy File   ${appsettings.default.json}  appsettings.json
    Remove Directory  ${cypnode_path}/keys     recursive=True
    Remove Directory  ${cypnode_path}/storedb  recursive=True
    ${cypnode_handle} =  Start Process  ${cypnode_path}/cypnode  stdout=stdout.txt  stderr=stderr.txt
    Sleep  10s
    Process Should Be Running  ${cypnode_handle}

    ${transaction_00}=    Get Binary File  ./integration/resources/transaction_00.raw
    ${transaction_01}=    Get Binary File  ./integration/resources/transaction_01.raw

    Create Session  nodeSession  http://127.0.0.1:7000
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_00}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  200
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_01}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  200
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_00}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  500
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_01}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  500
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_00}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  500
    
    ${response}=  POST On Session  nodeSession  ${transaction_endpoint}  data=${transaction_01}  headers=&{protobuf_headers}
    Status Should Be  200  ${response}
    Dictionary Should Contain Item  ${response.json()}  code  500
    
    Send Signal To Process  SIGINT  ${cypnode_handle}
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Contain  ${cypnode_result.stdout}  [FTL]
