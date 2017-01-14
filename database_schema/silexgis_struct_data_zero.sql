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
  `type_id` bigint(20) NOT NULL,
  `identification_code` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `description` text COLLATE utf8_unicode_ci,
  `user_id` bigint(20) unsigned NOT NULL,
  `other_toponyms` varchar(250) COLLATE utf8_unicode_ci DEFAULT NULL,
  `rock_type_id` bigint(20) DEFAULT NULL,
  `rock_age` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `hydrographic_basin` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  `valley` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `tributary_river` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `closest_address` varchar(200) COLLATE utf8_unicode_ci DEFAULT NULL,
  `is_show_cave` bit(1) DEFAULT NULL,
  `show_cave_length` smallint(6) DEFAULT NULL,
  `website` varchar(255) COLLATE utf8_unicode_ci DEFAULT NULL,
  `land_registry_number` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `region` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `depth` smallint(6) DEFAULT NULL,
  `surveyed_length` mediumint(9) DEFAULT NULL,
  `discovery_date` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `discoverer` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `volume` int(11) DEFAULT NULL,
  `positive_depth` smallint(6) DEFAULT NULL,
  `negative_depth` smallint(6) DEFAULT NULL,
  `ramification_index` tinyint(4) DEFAULT NULL,
  `real_extension` mediumint(9) DEFAULT NULL,
  `cave_age` int(11) DEFAULT NULL,
  `projected_extension` mediumint(9) DEFAULT NULL,
  `exploration_status` enum('Unknown','Not explored','Partially explored','Exploration finished') COLLATE utf8_unicode_ci DEFAULT NULL,
  `protection_class` varchar(20) COLLATE utf8_unicode_ci DEFAULT NULL,
  `potential_depth` smallint(6) DEFAULT NULL,
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

insert  into `entrance_types`(`id`,`name`) values (1,'cave'),(2,'pot'),(3,'abri'),(4,'well'),(5,'mine gallery');

/*Table structure for table `feature_types` */

DROP TABLE IF EXISTS `feature_types`;

CREATE TABLE `feature_types` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `symbol_path` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  `type` enum('point','linestring','polygon') COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=23 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `feature_types` */

insert  into `feature_types`(`id`,`name`,`symbol_path`,`type`) values (3,'cave','cave.png','point'),(19,'pot','pit.png','point'),(5,'sinkhole','sinkhole.png','point'),(6,'construction','','polygon'),(9,'lake',NULL,'polygon'),(10,'detritus',NULL,'polygon'),(11,'lapiezuri',NULL,'polygon'),(12,'peak',NULL,'point'),(22,'dry valley','fracture_line.png','polygon'),(14,'canion',NULL,'polygon'),(15,'ponor',NULL,'point'),(16,'portal',NULL,'point'),(17,'well',NULL,'point'),(18,'abrupt',NULL,'polygon'),(4,'cave entrance','cave.png','point'),(21,'fracture line','fracture_line.png','linestring');

/*Table structure for table `features` */

DROP TABLE IF EXISTS `features`;

CREATE TABLE `features` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  `point_id` bigint(20) NOT NULL,
  `description` varchar(1000) COLLATE utf8_unicode_ci DEFAULT NULL,
  `feature_type_id` bigint(20) NOT NULL,
  `properties` text COLLATE utf8_unicode_ci,
  `user_id` bigint(20) DEFAULT NULL,
  `add_time` datetime DEFAULT NULL,
  `tags` text COLLATE utf8_unicode_ci,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `features` */

/*Table structure for table `files` */

DROP TABLE IF EXISTS `files`;

CREATE TABLE `files` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_name` varchar(255) COLLATE utf8_unicode_ci NOT NULL,
  `user_id` bigint(20) NOT NULL,
  `add_time` datetime NOT NULL,
  `file_type` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `size` int(11) NOT NULL,
  `md5_hash` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `mime_type` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `files` */

/*Table structure for table `geofiles` */

DROP TABLE IF EXISTS `geofiles`;

CREATE TABLE `geofiles` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_name` varchar(255) COLLATE utf8_unicode_ci NOT NULL,
  `id_user` bigint(20) NOT NULL,
  `add_time` datetime NOT NULL,
  `type` enum('GPX','KML','undefined') COLLATE utf8_unicode_ci DEFAULT NULL,
  `size` int(11) NOT NULL,
  `md5_hash` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `geofiles` */

/*Table structure for table `geoobjects_to_files` */

DROP TABLE IF EXISTS `geoobjects_to_files`;

CREATE TABLE `geoobjects_to_files` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_id` bigint(20) unsigned NOT NULL,
  `geoobject_id` bigint(20) unsigned NOT NULL,
  `geoobject_type` enum('cave','feature','cave_entry') COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `file_geoobject_id` (`file_id`,`geoobject_id`,`geoobject_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `geoobjects_to_files` */

/*Table structure for table `images` */

DROP TABLE IF EXISTS `images`;

CREATE TABLE `images` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_path` varchar(2000) COLLATE utf8_unicode_ci NOT NULL,
  `user_id` bigint(20) DEFAULT NULL,
  `add_time` datetime DEFAULT NULL,
  `point_id` bigint(20) unsigned DEFAULT NULL,
  `description` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  `thumb_file_path` varchar(2000) COLLATE utf8_unicode_ci NOT NULL,
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

/*Table structure for table `map_views` */

DROP TABLE IF EXISTS `map_views`;

CREATE TABLE `map_views` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `properties` text COLLATE utf8_unicode_ci,
  `center_geometry` geometry DEFAULT NULL,
  `is_default` bit(1) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `map_views` */

/*Table structure for table `member_groups` */

DROP TABLE IF EXISTS `member_groups`;

CREATE TABLE `member_groups` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `member_groups` */

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
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `points` */

/*Table structure for table `pointsxx` */

DROP TABLE IF EXISTS `pointsxx`;

CREATE TABLE `pointsxx` (
  `id` bigint(20) unsigned NOT NULL DEFAULT '0',
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
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `pointsxx` */

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

/*Table structure for table `team_members` */

DROP TABLE IF EXISTS `team_members`;

CREATE TABLE `team_members` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `first_name` varchar(25) COLLATE utf8_unicode_ci DEFAULT NULL,
  `last_name` varchar(25) COLLATE utf8_unicode_ci DEFAULT NULL,
  `nickname` varchar(25) COLLATE utf8_unicode_ci DEFAULT NULL,
  `group_id` bigint(20) DEFAULT NULL,
  `picture_file_name` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `add_time` datetime DEFAULT NULL,
  `description` text COLLATE utf8_unicode_ci,
  `email` varchar(100) COLLATE utf8_unicode_ci DEFAULT NULL,
  `phone_number` varchar(25) COLLATE utf8_unicode_ci DEFAULT NULL,
  `notes` varchar(0) COLLATE utf8_unicode_ci DEFAULT NULL,
  `connected_user_id` bigint(20) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `team_members` */

/*Table structure for table `trip_logs` */

DROP TABLE IF EXISTS `trip_logs`;

CREATE TABLE `trip_logs` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `add_time` datetime DEFAULT NULL,
  `trip_start_time` datetime DEFAULT NULL,
  `trip_end_time` datetime DEFAULT NULL,
  `details` text COLLATE utf8_unicode_ci,
  `target_zone` varchar(90) COLLATE utf8_unicode_ci DEFAULT NULL,
  `type` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `temporary` bit(1) DEFAULT NULL,
  `summary` varchar(500) COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `trip_logs` */

/*Table structure for table `trip_logs_to_features` */

DROP TABLE IF EXISTS `trip_logs_to_features`;

CREATE TABLE `trip_logs_to_features` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `geoobject_id` bigint(20) unsigned NOT NULL,
  `trip_log_id` bigint(20) unsigned NOT NULL,
  `geoobject_type` enum('cave','feature','cave_entrance') COLLATE utf8_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `trip_feature_unique` (`geoobject_id`,`trip_log_id`,`geoobject_type`)
) ENGINE=InnoDB AUTO_INCREMENT=37 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `trip_logs_to_features` */

insert  into `trip_logs_to_features`(`id`,`geoobject_id`,`trip_log_id`,`geoobject_type`) values (35,2,3,'cave'),(36,6,3,'cave');

/*Table structure for table `trip_logs_to_files` */

DROP TABLE IF EXISTS `trip_logs_to_files`;

CREATE TABLE `trip_logs_to_files` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `file_id` bigint(20) NOT NULL,
  `trip_log_id` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `trip_file_unique` (`file_id`,`trip_log_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `trip_logs_to_files` */

/*Table structure for table `trip_logs_to_team_members` */

DROP TABLE IF EXISTS `trip_logs_to_team_members`;

CREATE TABLE `trip_logs_to_team_members` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `id_team_member` bigint(20) NOT NULL,
  `id_trip_log` bigint(20) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `UNIQUE_MEMBER_IN_TRIP` (`id_team_member`,`id_trip_log`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `trip_logs_to_team_members` */

/*Table structure for table `users` */

DROP TABLE IF EXISTS `users`;

CREATE TABLE `users` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `password` varchar(50) COLLATE utf8_unicode_ci NOT NULL,
  `email` varchar(50) COLLATE utf8_unicode_ci DEFAULT NULL,
  `admin_level` int(11) DEFAULT NULL,
  `language` varchar(5) COLLATE utf8_unicode_ci DEFAULT NULL,
  `last_log_in_time` datetime DEFAULT NULL,
  `add_time` datetime NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=6 DEFAULT CHARSET=utf8 COLLATE=utf8_unicode_ci;

/*Data for the table `users` */

insert  into `users`(`id`,`username`,`password`,`email`,`admin_level`,`language`,`last_log_in_time`,`add_time`) values (1,'user','pass','',0,NULL,NULL,'0000-00-00 00:00:00'),(3,'user2','pass','',0,NULL,NULL,'0000-00-00 00:00:00'),(4,'admin','pass','admin@silexgis.com',0,NULL,NULL,'0000-00-00 00:00:00'),(5,'','',NULL,NULL,NULL,NULL,'0000-00-00 00:00:00');

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
