/*
SQLyog Community v12.02 (32 bit)
MySQL - 5.5.24 : Database - speogis
*********************************************************************
*/

/*!40101 SET NAMES utf8 */;

/*!40101 SET SQL_MODE=''*/;

/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
CREATE DATABASE /*!32312 IF NOT EXISTS*/`speogis` /*!40100 DEFAULT CHARACTER SET utf8 COLLATE utf8_unicode_ci */;

USE `speogis`;

/*Table structure for table `cave_entrances` */

DROP TABLE IF EXISTS `cave_entrances`;

CREATE TABLE `cave_entrances` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  `point_id` bigint(20) DEFAULT NULL,
  `entranceType` bigint(20) NOT NULL,
  `description` varchar(2000) COLLATE utf8_unicode_ci DEFAULT NULL,
  `is_main_entrance` bit(1) DEFAULT NULL,
  `hydrologic_type` bigint(20) DEFAULT NULL,
  `cave_id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `cave_entrances` */

/*Table structure for table `cave_types` */

DROP TABLE IF EXISTS `cave_types`;

CREATE TABLE `cave_types` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(40) COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=10 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `cave_types` */

insert  into `cave_types`(`id`,`name`) values (1,'pestera'),(2,'aven'),(3,'mixt'),(4,'abri'),(5,'ponor'),(6,'mina'),(7,'mina-prospectiune'),(8,'?pestera-aven'),(9,'?aven-pestera');

/*Table structure for table `caves` */

DROP TABLE IF EXISTS `caves`;

CREATE TABLE `caves` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(100) COLLATE utf8_unicode_ci NOT NULL,
  `typeId` bigint(20) NOT NULL,
  `locationIdentifier` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `description` text COLLATE utf8_unicode_ci,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `caves` */

/*Table structure for table `entrance_types` */

DROP TABLE IF EXISTS `entrance_types`;

CREATE TABLE `entrance_types` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(40) COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `entrance_types` */

insert  into `entrance_types`(`id`,`name`) values (1,'pestera'),(2,'aven'),(3,'abri'),(4,'ponor'),(5,'mina-galerie');

/*Table structure for table `feature_types` */

DROP TABLE IF EXISTS `feature_types`;

CREATE TABLE `feature_types` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `symbol_path` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  `type` enum('point','linestring','polygon') COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=19 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `feature_types` */

insert  into `feature_types`(`id`,`name`,`symbol_path`,`type`) values (3,'pestera','cave.png','point'),(4,'aven','pit.png','point'),(5,'dolina','sinkhole.png','point'),(6,'constructie','','polygon'),(7,'colti',NULL,'linestring'),(8,'abri',NULL,'point'),(9,'lac',NULL,'polygon'),(10,'grohotis',NULL,'polygon'),(11,'lapiezuri',NULL,'polygon'),(12,'varf',NULL,'point'),(13,'uvala',NULL,'polygon'),(14,'canion',NULL,'polygon'),(15,'ponor',NULL,'point'),(16,'portal',NULL,'point'),(17,'gaura',NULL,'point'),(18,'abrupt',NULL,'polygon');

/*Table structure for table `features` */

DROP TABLE IF EXISTS `features`;

CREATE TABLE `features` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  `point_id` bigint(20) NOT NULL,
  `description` varchar(1000) COLLATE utf8_unicode_ci DEFAULT NULL,
  `feature_type_id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=32 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `features` */

insert  into `features`(`id`,`name`,`point_id`,`description`,`feature_type_id`) values (31,'dolina cea mai cea',8,'',5),(17,'qqqqqqq2',37,'',5),(18,'qqqq',38,'',5),(19,'qw1',39,'',5),(20,'qqqqqqqqqq',40,'',5),(21,'q',41,'',5),(22,'q',42,'',4),(23,'cons',43,'',6),(24,'aven1',1,'',4),(25,'con',2,'',6),(26,'colti',3,'',7),(27,'lac',4,'',9),(28,'dolina',5,'',5),(29,'uv',6,'',13),(30,'zzzzzzzz',7,'',4);

/*Table structure for table `files` */

DROP TABLE IF EXISTS `files`;

CREATE TABLE `files` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_name` varchar(255) COLLATE utf8_unicode_ci NOT NULL,
  `id_user` bigint(20) NOT NULL,
  `add_time` datetime NOT NULL,
  `type` enum('GPX','KML','undefined') COLLATE utf8_unicode_ci DEFAULT NULL,
  `size` int(11) NOT NULL,
  `md5_hash` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=63 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `files` */

insert  into `files`(`id`,`file_name`,`id_user`,`add_time`,`type`,`size`,`md5_hash`) values (1,'.x_Waypoints.gpx',1,'2016-05-05 12:22:29','undefined',0,NULL),(2,'.x_Waypoints.gpx',1,'2016-05-05 12:22:40','undefined',0,NULL),(3,'.x_Waypoints.gpx',1,'2016-05-05 12:27:00','undefined',0,NULL),(4,'.53.gpx',1,'2016-05-05 12:27:30','undefined',0,NULL),(5,'x_Waypoints.gpx',1,'2016-05-05 12:44:30','undefined',2314,'e3f39eb56909df4c1a2d75f66'),(6,'x_Waypoints.gpx',1,'2016-05-05 12:46:36','undefined',2314,'e3f39eb56909df4c1a2d75f66bec9dab'),(7,'ovCurrent.gpx',1,'2016-05-05 12:46:44','undefined',1826337,'3731b3e119ea7ccde68dd919e687faf1'),(19,'uploads/71.gpx',1,'2016-05-31 18:03:25','undefined',838846,'687a620335bf96df05a2fc103593aee9'),(20,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:46:55','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(21,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:52:42','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(22,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:55:39','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(23,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:58:30','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(24,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:58:59','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(25,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 06:59:46','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(26,'uploads/saveGPSFileData.php',1,'2016-06-24 07:00:32','undefined',2421,'1f08fe4ef7988b64ac14a3d71b694745'),(27,'uploads/saveGPSFileData.php',1,'2016-06-24 07:01:09','undefined',2422,'a552cfad5f8eaacbd7fde5ba37d41820'),(28,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:02:01','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(29,'uploads/saveGPSFileData.php',1,'2016-06-24 07:02:17','undefined',2412,'5218ba9441a7abda0838a91521e440b5'),(30,'uploads/saveGPSFileData.php',1,'2016-06-24 07:02:37','undefined',2414,'52979e14fd6a1eb429dbd907f1cf7cbb'),(31,'uploads/saveGPSFileData.php',1,'2016-06-24 07:03:03','undefined',2402,'d6d7f7cf73a0cbb901bf9274440b2ca9'),(32,'uploads/saveGPSFileData.php',1,'2016-06-24 07:03:20','undefined',2401,'07e40114a92e79b8f7025e648dd127c7'),(33,'uploads/saveGPSFileData.php',1,'2016-06-24 07:03:43','undefined',2402,'3536b73787dc94bcba93dc5aa85b025e'),(34,'uploads/saveGPSFileData.php',1,'2016-06-24 07:04:18','undefined',2403,'0dfffafdf3c5d8897bd216ba51cd2f1c'),(35,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:06:16','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(36,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:09:31','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(37,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:09:57','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(38,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:23:39','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(39,'uploads/persani_ov_adi.gpx',1,'2016-06-24 07:26:00','undefined',237185,'29209984ecbe87070b7d2a79059c010c'),(40,'uploads/garminCurrent.gpx',1,'2016-06-24 07:26:17','undefined',229327,'d9820918ec394b2fc92c2fc768ddd8aa'),(41,'uploads/garminCurrent_drum_comana_de_sus.gpx',1,'2016-06-24 07:31:10','undefined',229327,'d9820918ec394b2fc92c2fc768ddd8aa'),(42,'uploads/persani_ov_adi.gpx',1,'2016-06-24 07:31:21','undefined',237185,'29209984ecbe87070b7d2a79059c010c'),(43,'uploads/persani_ov_adi.gpx',1,'2016-06-24 07:35:43','undefined',237185,'29209984ecbe87070b7d2a79059c010c'),(44,'uploads/55.gpx',1,'2016-06-24 07:35:51','undefined',1057582,'619f9cb884a3268a656eb33f2d7e2b67'),(45,'uploads/ovCurrent.gpx',1,'2016-06-24 07:38:22','undefined',1826337,'3731b3e119ea7ccde68dd919e687faf1'),(46,'uploads/Jun_23,_2016_09_22_52.gpx',1,'2016-06-24 07:41:37','undefined',158485,'29977fe529372edf434a2c13293b9f1a'),(47,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-24 14:07:35','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(48,'uploads/2046-81_ponor_suspendat.kml',1,'2016-06-24 18:19:18','undefined',24028,'ae16a6fd75eeb437860472bb85c72ad6'),(49,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-24 18:58:11','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(50,'uploads/2046-81_ponor_suspendat.kml',1,'2016-06-24 19:05:39','undefined',24028,'ae16a6fd75eeb437860472bb85c72ad6'),(51,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-27 12:15:07','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(52,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-27 12:24:46','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(53,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-27 12:24:56','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(54,'uploads/Jun_23,_2016_09_22_52.kml',1,'2016-06-27 12:27:17','undefined',136602,'0da2aa4608825bb7e322a9ddc00d3b58'),(55,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 00:48:11','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(56,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 00:49:01','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(57,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 01:07:24','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(58,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 01:09:18','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(59,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 01:16:32','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(60,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 01:18:35','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(61,'uploads/2046-81_ponor_suspendat_02072016.kml',1,'2016-07-06 09:21:50','undefined',26200,'19ca259d56595eb6d0c9758adaa9f601'),(62,'uploads/2016-07-03_10-27-01.gpx',1,'2016-07-06 09:26:38','undefined',174673,'26a6dfa229fb3bf0d91d4cefb0216239');

/*Table structure for table `geom2` */

DROP TABLE IF EXISTS `geom2`;

CREATE TABLE `geom2` (
  `g` geometry DEFAULT NULL,
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `geom2` */

insert  into `geom2`(`g`,`id`) values ('\0\0\0\0\0\0\0\0\0\0\0\0\0ð?\0\0\0\0\0\0ð?',1),('\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0ð?\0\0\0\0\0\0ð?\0\0\0\0\0\0\0@\0\0\0\0\0\0\0@',2),('\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0ð?\0\0\0\0\0\0ð?\0\0\0\0\0\0\0@\0\0\0\0\0\0\0@',3),('\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0ð?\0\0\0\0\0\0ð?\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0ð?\0\0\0\0\0\0ð?\0\0\0\0\0\0\0@\0\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@',4),('\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0$@\0\0\0\0\0\0\0\0\0\0\0\0\0\0$@\0\0\0\0\0\0$@\0\0\0\0\0\0\0\0\0\0\0\0\0\0$@\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@\0\0\0\0\0\0@',5);

/*Table structure for table `images` */

DROP TABLE IF EXISTS `images`;

CREATE TABLE `images` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_path` varchar(2000) COLLATE utf8_unicode_ci NOT NULL,
  `user_id` bigint(20) DEFAULT NULL,
  `add_time` datetime DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `images` */

/*Table structure for table `log` */

DROP TABLE IF EXISTS `log`;

CREATE TABLE `log` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `log` */

/*Table structure for table `nodes` */

DROP TABLE IF EXISTS `nodes`;

CREATE TABLE `nodes` (
  `id` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `nodes` */

/*Table structure for table `points` */

DROP TABLE IF EXISTS `points`;

CREATE TABLE `points` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `lat` double(9,6) DEFAULT NULL,
  `long` double(9,6) DEFAULT NULL,
  `elevation` int(11) DEFAULT NULL,
  `coords` point NOT NULL,
  `gpx_name` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  `gpx_sym` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `gpx_type` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `gpx_cmt` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  `gpx_sat` int(11) DEFAULT NULL,
  `gpx_fix` varchar(8) COLLATE utf8_unicode_ci DEFAULT NULL,
  `gpx_time` datetime DEFAULT NULL,
  `_type` int(11) DEFAULT NULL,
  `_details` varchar(5000) COLLATE utf8_unicode_ci DEFAULT NULL,
  `added_by_user_id` bigint(20) unsigned NOT NULL,
  `add_time` datetime NOT NULL,
  `_id_point_type` bigint(20) DEFAULT NULL,
  `spatial_geometry` geometry DEFAULT NULL,
  PRIMARY KEY (`id`),
  SPATIAL KEY `sx_mytable_coords` (`coords`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `points` */

/*Table structure for table `tags` */

DROP TABLE IF EXISTS `tags`;

CREATE TABLE `tags` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `type` enum('node','way','relation') COLLATE utf8_unicode_ci DEFAULT NULL,
  `k` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `v` varchar(0) COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `tags` */

/*Table structure for table `users` */

DROP TABLE IF EXISTS `users`;

CREATE TABLE `users` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `password` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `email` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `admin_level` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=4 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `users` */

insert  into `users`(`id`,`username`,`password`,`email`,`admin_level`) values (1,'user','pass','',0),(3,'user2','pass','',0);

/*Table structure for table `ways` */

DROP TABLE IF EXISTS `ways`;

CREATE TABLE `ways` (
  `id` bigint(11) unsigned NOT NULL AUTO_INCREMENT,
  `visible` tinyint(1) DEFAULT NULL,
  `version` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `ways` */

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;
