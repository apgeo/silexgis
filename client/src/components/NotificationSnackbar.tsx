import * as React from 'react';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Snackbar from '@mui/material/Snackbar';
import IconButton from '@mui/material/IconButton';
import CloseIcon from '@mui/icons-material/Close';

import MuiAlert, { AlertProps } from '@mui/material/Alert';

const Alert = React.forwardRef<HTMLDivElement, AlertProps>(function Alert(
  props,
  ref,
) {
  return <MuiAlert elevation={6} ref={ref} variant="filled" {...props} />;
});

export default function NotificationSnackbar(props) {
  // const [open, setOpen] = React.useState(/*false*/props.isNotificationOpen);
  // probably [open, setOpen] will just duplicate state from the parent compoenent if used in tandem with it

  // const handleClick = () => {
  //   setOpen(true);
  // };

  const handleClose = (event?: React.SyntheticEvent | Event, reason?: string) => {

    if (reason === 'clickaway') {
      return;
    }

    // setOpen(false);
    props.onOpenStateChange(false);
  };

  const action = (
    <React.Fragment>
      <Button color="secondary" size="small" onClick={handleClose}>
        UNDO
      </Button>
      <IconButton
        size="small"
        aria-label="close"
        color="inherit"
        onClick={handleClose}
      >
        <CloseIcon fontSize="small" />
      </IconButton>
    </React.Fragment>
  );

  return (
    <div>
      {/* <Button onClick={handleClick}>Open simple snackbar</Button> */}
      <Stack spacing={2} sx={{ width: '100%' }}>
      {/* open={open} */}
        <Snackbar
          open={props.isOpen}
          autoHideDuration={4000}
          onClose={handleClose}
          message="{props.message}"
          action={action}

          anchorOrigin={ { vertical: 'bottom', horizontal: 'right' } }
        >
          <Alert onClose={handleClose} severity={props.alertSeverity} sx={{ width: '100%' }}>
            {props.message}
          </Alert>
        </Snackbar>

        {/* <Alert severity="error">This is an error message!</Alert>
        <Alert severity="warning">This is a warning message!</Alert>
        <Alert severity="info">This is an information message!</Alert>
        <Alert severity="success">{props.message}</Alert> */}
      </Stack>
    </div>
  );
}
