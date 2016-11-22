<?php
  include_once 'config.php'; include_once 'utilities.php';
  
  function DB_Connect($show_error = 1)
  {
      $conn_id = mysql_connect(DB_HOST, DB_USER, DB_PASS) or die (mysql_error());      
      if ($show_error == 1) { mysql_selectdb(DB_NAME) or die (mysql_error()); } else return "mysql_error_no ".mysql_error(); if ($show_error == 4) Utils::redirect_to_page("error.html");
      
      return $conn_id;
  }
  
  function DB_Free($conn_id)
  {
        mysql_close($conn_id);
  }
  
  function DB_Execute($conn_id, $query, $show_errors = 1)
  {       /*$show_errors = 1;*/                      $err = -1;
      //$st = null;global $total_execution_time;
      //if (defined("SHOW_EXECUTION_TIME") && SHOW_EXECUTION_TIME)
        //$st = Utils::start_timer();
      //echo "$query <br/>"; //if ($_GET[])
      if ($result = mysql_query($query, $conn_id))
      { if (@$_GET['show_query']) echo "$query <br/>";
        //if (defined("SHOW_EXECUTION_TIME") && SHOW_EXECUTION_TIME){
        //    /*$time = Utils::get_timer_seconds($st); echo number_format($time, 4, ".", ',')." seconds is the running time for query \"$query\"<br/><br/>"; $total_execution_time += $time; echo "total: ".$total_execution_time."<br/>";*/}

        return $result;
      }
      else
      if ($show_errors == 1)
            Utils::print_message(mysql_errno($conn_id)." ".mysql_error($conn_id)."  ".$query, "__DB ", MSG_ERROR);
            else 
                if (($show_errors == 2) && (($err = mysql_errno($conn_id)) != 1062)) //show only if the error is not Duplicate key
                    Utils::print_message("dup>".$err." ".mysql_error($conn_id)."  ".$query, "__DB ");
                    else
                        if ($show_errors == 4)
                            Utils::redirect_to_page("error.html");//return "mysql_error_no ".mysql_errno();
      
      return false;
  }
  
  function DB_BeginTransaction($conn_id)
  {
      if (!$result = mysql_query('BEGIN', $conn_id))
        Utils::print_message(mysql_error($conn_id), " __Database fatal error");
  }
  
  function DB_CommitTransaction($conn_id)
  {
      if (!$result = mysql_query('COMMIT', $conn_id))
        Utils::print_message(mysql_error($conn_id), " __Database fatal error");
  }

   class DBCon
   {

        static function open_connection()
        {
            global $CONN_ID;
            //echo ">> connection opened ".$CONN_ID;
            if (empty($CONN_ID) || @!mysql_ping($CONN_ID))
            {
                $CONN_ID = DB_Connect();
                
                if (is_string($CONN_ID) && (strpos($CONN_ID, 'mysql_error_no') !== false)) 
                    { echo "db connection error: $CONN_ID <br/>"; return null; } else return $CONN_ID; 
                
                //DB_BeginTransaction($CONN_ID); 
                return $CONN_ID;
            } 
            
            return $CONN_ID;
        }

        static function close_connection()
        {
            global $CONN_ID;
            //echo ">> connection closed ".$CONN_ID;
            //DB_CommitTransaction($CONN_ID);
            DB_Free($CONN_ID);
        }

        static function get_connection_id() 
        { 
            global $CONN_ID; // todo make the global variable staic inside this class
            return $CONN_ID;
        }
   }  

   ?>