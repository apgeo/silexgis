
# This is a fix for InnoDB in MySQL >= 4.1.x
# It "suspends judgement" for fkey relationships until are tables are set.
SET FOREIGN_KEY_CHECKS = 0;

-- ---------------------------------------------------------------------
-- cave_entrances
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `cave_entrances`;

CREATE TABLE `cave_entrances`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(100),
    `point_id` BIGINT,
    `entranceType` BIGINT NOT NULL,
    `description` VARCHAR(2000),
    `is_main_entrance` bit(1),
    `hydrologic_type` BIGINT,
    `cave_id` BIGINT NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- cave_types
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `cave_types`;

CREATE TABLE `cave_types`
(
    `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(40) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- caves
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `caves`;

CREATE TABLE `caves`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(100) NOT NULL,
    `type_id` BIGINT NOT NULL,
    `identification_code` VARCHAR(50),
    `description` TEXT,
    `user_id` bigint(20) unsigned NOT NULL,
    `other_toponyms` VARCHAR(250),
    `rock_type_id` BIGINT,
    `rock_age` VARCHAR(50),
    `hydrographic_basin` VARCHAR(100),
    `valley` VARCHAR(50),
    `tributary_river` VARCHAR(50),
    `closest_address` VARCHAR(200),
    `is_show_cave` bit(1),
    `show_cave_length` SMALLINT,
    `website` VARCHAR(255),
    `land_registry_number` VARCHAR(50),
    `region` VARCHAR(50),
    `depth` SMALLINT,
    `surveyed_length` SMALLINT(9),
    `discovery_date` VARCHAR(50),
    `discoverer` VARCHAR(50),
    `volume` INTEGER,
    `positive_depth` SMALLINT,
    `negative_depth` SMALLINT,
    `ramification_index` TINYINT,
    `real_extension` SMALLINT(9),
    `cave_age` INTEGER,
    `projected_extension` SMALLINT(9),
    `exploration_status` enum('Unknown','Not explored','Partially explored','Exploration finished'),
    `protection_class` VARCHAR(20),
    `potential_depth` SMALLINT,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- entrance_types
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `entrance_types`;

CREATE TABLE `entrance_types`
(
    `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(40) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- feature_types
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `feature_types`;

CREATE TABLE `feature_types`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(50) NOT NULL,
    `symbol_path` VARCHAR(500),
    `type` enum('point','linestring','polygon') NOT NULL,
    `group_type` VARCHAR(20),
    `style_properties` TEXT,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- features
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `features`;

CREATE TABLE `features`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(100),
    `point_id` BIGINT NOT NULL,
    `description` VARCHAR(1000),
    `feature_type_id` BIGINT NOT NULL,
    `properties` TEXT,
    `user_id` BIGINT,
    `add_time` DATETIME,
    `tags` TEXT,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- files
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `files`;

CREATE TABLE `files`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `file_name` VARCHAR(255) NOT NULL,
    `user_id` BIGINT NOT NULL,
    `add_time` DATETIME NOT NULL,
    `file_type` VARCHAR(50),
    `size` INTEGER NOT NULL,
    `md5_hash` VARCHAR(50),
    `mime_type` VARCHAR(100),
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- geofiles
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `geofiles`;

CREATE TABLE `geofiles`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `file_name` VARCHAR(255) NOT NULL,
    `id_user` BIGINT NOT NULL,
    `add_time` DATETIME NOT NULL,
    `type` enum('GPX','KML','undefined'),
    `size` INTEGER NOT NULL,
    `md5_hash` VARCHAR(50),
    `enabled` bit(1) DEFAULT 'b\'1\'' NOT NULL,
    `extract_style` bit(1) DEFAULT 'b\'1\'' NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- geoobjects_to_files
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `geoobjects_to_files`;

CREATE TABLE `geoobjects_to_files`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `file_id` bigint(20) unsigned NOT NULL,
    `geoobject_id` bigint(20) unsigned NOT NULL,
    `geoobject_type` enum('cave','feature','cave_entry') NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE INDEX `file_geoobject_id` (`file_id`, `geoobject_id`, `geoobject_type`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- georeferenced_maps
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `georeferenced_maps`;

CREATE TABLE `georeferenced_maps`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `description` VARCHAR(500),
    `boundary_north` DECIMAL(9,6) NOT NULL,
    `boundary_east` DECIMAL(9,6) NOT NULL,
    `boundary_south` DECIMAL(9,6) NOT NULL,
    `boundary_west` decimal(9,6) unsigned NOT NULL,
    `image_id` BIGINT NOT NULL,
    `enabled` bit(1) DEFAULT 'b\'1\'' NOT NULL,
    `title` VARCHAR(90),
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- images
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `images`;

CREATE TABLE `images`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `file_path` VARCHAR(2000) NOT NULL,
    `user_id` BIGINT,
    `add_time` DATETIME,
    `point_id` bigint(20) unsigned,
    `description` VARCHAR(500),
    `thumb_file_path` VARCHAR(2000) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- log
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `log`;

CREATE TABLE `log`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- map_views
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `map_views`;

CREATE TABLE `map_views`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(50) NOT NULL,
    `properties` TEXT,
    `center_geometry` geometry,
    `is_default` bit(1),
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- member_groups
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `member_groups`;

CREATE TABLE `member_groups`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(50) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- nodes
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `nodes`;

CREATE TABLE `nodes`
(
    `id` INTEGER NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- points
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `points`;

CREATE TABLE `points`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `lat` DOUBLE(9,6),
    `long` DOUBLE(9,6),
    `elevation` INTEGER,
    `gpx_name` VARCHAR(500),
    `gpx_sym` VARCHAR(50),
    `gpx_type` VARCHAR(50),
    `gpx_cmt` VARCHAR(500),
    `gpx_sat` INTEGER,
    `gpx_fix` VARCHAR(8),
    `gpx_time` DATETIME,
    `_type` INTEGER,
    `_details` VARCHAR(5000),
    `added_by_user_id` bigint(20) unsigned NOT NULL,
    `add_time` DATETIME NOT NULL,
    `_id_point_type` BIGINT,
    `spatial_geometry` geometry,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- pointsxx
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `pointsxx`;

CREATE TABLE `pointsxx`
(
    `id` bigint(20) unsigned DEFAULT 0 NOT NULL,
    `lat` DOUBLE(9,6),
    `long` DOUBLE(9,6),
    `elevation` INTEGER,
    `coords` point NOT NULL,
    `gpx_name` VARCHAR(500),
    `gpx_sym` VARCHAR(50),
    `gpx_type` VARCHAR(50),
    `gpx_cmt` VARCHAR(500),
    `gpx_sat` INTEGER,
    `gpx_fix` VARCHAR(8),
    `gpx_time` DATETIME,
    `_type` INTEGER,
    `_details` VARCHAR(5000),
    `added_by_user_id` bigint(20) unsigned NOT NULL,
    `add_time` DATETIME NOT NULL,
    `_id_point_type` BIGINT,
    `spatial_geometry` geometry,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- tags
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `tags`;

CREATE TABLE `tags`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `type` enum('node','way','relation'),
    `k` VARCHAR(50) NOT NULL,
    `v` VARCHAR(255),
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- team_members
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `team_members`;

CREATE TABLE `team_members`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `first_name` VARCHAR(25),
    `last_name` VARCHAR(25),
    `nickname` VARCHAR(25),
    `group_id` BIGINT,
    `picture_file_name` VARCHAR(50),
    `add_time` DATETIME,
    `description` TEXT,
    `email` VARCHAR(100),
    `phone_number` VARCHAR(25),
    `notes` VARCHAR(255),
    `connected_user_id` BIGINT,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- trip_logs
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `trip_logs`;

CREATE TABLE `trip_logs`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `add_time` DATETIME,
    `trip_start_time` DATETIME,
    `trip_end_time` DATETIME,
    `details` TEXT,
    `target_zone` VARCHAR(90),
    `type` VARCHAR(50),
    `temporary` bit(1),
    `summary` VARCHAR(500),
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- trip_logs_to_features
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `trip_logs_to_features`;

CREATE TABLE `trip_logs_to_features`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `geoobject_id` bigint(20) unsigned NOT NULL,
    `trip_log_id` bigint(20) unsigned NOT NULL,
    `geoobject_type` enum('cave','feature','cave_entrance'),
    PRIMARY KEY (`id`),
    UNIQUE INDEX `trip_feature_unique` (`geoobject_id`, `trip_log_id`, `geoobject_type`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- trip_logs_to_files
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `trip_logs_to_files`;

CREATE TABLE `trip_logs_to_files`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `file_id` BIGINT NOT NULL,
    `trip_log_id` BIGINT NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE INDEX `trip_file_unique` (`file_id`, `trip_log_id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- trip_logs_to_team_members
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `trip_logs_to_team_members`;

CREATE TABLE `trip_logs_to_team_members`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `id_team_member` BIGINT NOT NULL,
    `id_trip_log` BIGINT NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE INDEX `UNIQUE_MEMBER_IN_TRIP` (`id_team_member`, `id_trip_log`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- users
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `users`;

CREATE TABLE `users`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `username` VARCHAR(50) NOT NULL,
    `password` VARCHAR(50) NOT NULL,
    `email` VARCHAR(50),
    `admin_level` INTEGER,
    `language` VARCHAR(5),
    `last_log_in_time` DATETIME,
    `add_time` DATETIME,
    `picture_storage_type` INTEGER,
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

-- ---------------------------------------------------------------------
-- ways
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `ways`;

CREATE TABLE `ways`
(
    `id` bigint(11) unsigned NOT NULL AUTO_INCREMENT,
    `visible` TINYINT(1),
    `version` INTEGER,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

# This restores the fkey checks, after having unset them earlier
SET FOREIGN_KEY_CHECKS = 1;
