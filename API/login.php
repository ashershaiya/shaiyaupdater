<?php
header('Content-Type: application/json');


if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    echo json_encode(array("success" => false, "message" => "Invalid request method."));
    exit;
}

require_once 'config.php';


$inputJSON = file_get_contents('php://input');
$input = json_decode($inputJSON, true);


$username = isset($input['username']) ? $input['username'] : (isset($_POST['username']) ? $_POST['username'] : '');
$password = isset($input['password']) ? $input['password'] : (isset($_POST['password']) ? $_POST['password'] : '');

if (empty($username) || empty($password)) {
    echo json_encode(array("success" => false, "message" => "Please enter your username and password."));
    odbc_close($conn);
    exit;
}

$userUID = 0;
$loggedInUserId = '';
$loggedInPoints = 0;


$sql = "SELECT UserUID, UserID, Pw, Point FROM PS_UserData.dbo.Users_Master WHERE UserID = ?";
$stmt = odbc_prepare($conn, $sql);

if ($stmt === false) {
    echo json_encode(array("success" => false, "message" => "Database error."));
    odbc_close($conn);
    exit;
}

$result = odbc_execute($stmt, array($username));

if ($result === false) {
    echo json_encode(array("success" => false, "message" => "Database error."));
    odbc_close($conn);
    exit;
}

if ($row = odbc_fetch_array($stmt)) {
    $dbPassword = isset($row['Pw']) ? $row['Pw'] : '';
    if ($dbPassword === $password) {
        $loggedInUserId = isset($row['UserID']) ? $row['UserID'] : $username;
        $loggedInPoints = isset($row['Point']) ? (int)$row['Point'] : 0;
        $userUID = isset($row['UserUID']) ? (int)$row['UserUID'] : 0;
    } else {
        echo json_encode(array("success" => false, "message" => "Invalid password."));
        odbc_close($conn);
        exit;
    }
} else {
    echo json_encode(array("success" => false, "message" => "Account not found."));
    odbc_close($conn);
    exit;
}


$factionCountry = 2; 
if ($userUID > 0) {
    $factionSql = "SELECT TOP 1 Country FROM PS_GameData.dbo.UserMaxGrow WHERE UserUID = ?";
    $factionStmt = odbc_prepare($conn, $factionSql);

    if ($factionStmt !== false) {
        $factionResult = odbc_execute($factionStmt, array($userUID));
        if ($factionResult !== false) {
            if ($factionRow = odbc_fetch_array($factionStmt)) {
                $factionCountry = isset($factionRow['Country']) ? (int)$factionRow['Country'] : 2;
            }
        }
    }
}

odbc_close($conn);


echo json_encode(array(
    "success" => true,
    "userId" => $loggedInUserId,
    "points" => $loggedInPoints,
    "factionCountry" => $factionCountry
));
?>
