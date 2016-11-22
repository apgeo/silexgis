
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
    `pointId` BIGINT,
    `entranceType` BIGINT NOT NULL,
    `description` VARCHAR(2000),
    `isMainEntrance` bit(1),
    `hydrologicType` BIGINT,
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
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- caves
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `caves`;

CREATE TABLE `caves`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(100) NOT NULL,
    `typeId` BIGINT NOT NULL,
    `locationIdentifier` VARCHAR(50),
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
-- features
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `features`;

CREATE TABLE `features`
(
    `id` BIGINT NOT NULL,
    `name` VARCHAR(100),
    `id_point` BIGINT NOT NULL,
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
    `id_user` BIGINT NOT NULL,
    `add_time` DATETIME NOT NULL,
    `type` enum('GPX','KML','undefined'),
    `size` INTEGER NOT NULL,
    `md5_hash` VARCHAR(50),
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
-- nodes
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `nodes`;

CREATE TABLE `nodes`
(
    `id` INTEGER NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- point_types
-- ---------------------------------------------------------------------

DROP TABLE IF EXISTS `point_types`;

CREATE TABLE `point_types`
(
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(50) NOT NULL,
    `symbol_path` VARCHAR(500),
    PRIMARY KEY (`id`)
) ENGINE=MyISAM;

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
    PRIMARY KEY (`id`),
    INDEX `sx_mytable_coords` (`coords`)
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
