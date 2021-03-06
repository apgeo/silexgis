<?php
/* vim: set expandtab tabstop=4 shiftwidth=4: */
// +----------------------------------------------------------------------+
// | PHP Version 4                                                        |
// +----------------------------------------------------------------------+
// | Copyright (c) 1997-2004 The PHP Group                                |
// +----------------------------------------------------------------------+
// | This source file is subject to version 3.0 of the PHP license,       |
// | that is bundled with this package in the file LICENSE, and is        |
// | available at through the world-wide-web at                           |
// | http://www.php.net/license/3_0.txt.                                  |
// | If you did not receive a copy of the PHP license and are unable to   |
// | obtain it through the world-wide-web, please send a note to          |
// | license@php.net so we can mail you a copy immediately.               |
// +----------------------------------------------------------------------+
// | Authors: Aidan Lister <aidan@php.net>                                |
// +----------------------------------------------------------------------+
//
// $Id: fprintf.php,v 1.7 2004/06/12 06:53:00 aidan Exp $
//


/**
 * Replace fprintf()
 *
 * @category    PHP
 * @package     PHP_Compat
 * @link        http://php.net/function.fprintf
 * @author      Aidan Lister <aidan@php.net>
 * @version     $Revision: 1.7 $
 * @since       PHP 5
 */
if (!function_exists('fprintf'))
{
   function fprintf () {
        $args = func_get_args();

        if (count($args) < 2) {
            trigger_error ('Wrong parameter count for fprintf()', E_USER_WARNING);
            return null;
        }

        $resource_handle = array_shift($args);
        $format = array_shift($args);

		if (!is_resource($resource_handle)) {
			trigger_error ('fprintf(): supplied argument is not a valid stream resource', E_USER_WARNING);
			return false;
		}

        return fwrite($resource_handle, vsprintf($format, $args));
   }
}

?>