<?php

$dsn = "Driver={SQL Server};Server=127.0.0.1;Database=PS_UserData;";
$dbUser = "shaiya";
$dbPass = "Shaiya123";


$conn = odbc_connect($dsn, $dbUser, $dbPass);

if ($conn === false) {
    $errorMsg = "Database connection error.";
    $odbcError = odbc_errormsg();
    if (!empty($odbcError)) {
        $errorMsg .= " " . $odbcError;
    }

    header('Content-Type: application/json');
    echo json_encode(array(
        "success" => false,
        "message" => $errorMsg
    ));
    exit;
}
?>
